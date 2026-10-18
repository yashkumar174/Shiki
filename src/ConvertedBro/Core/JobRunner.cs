using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ConvertedBro.Core;

public enum JobState { Queued, Running, Done, Failed, Canceled }

/// <summary>One action applied to a batch of files. Raises <see cref="Changed"/> from any thread.</summary>
public sealed class Job
{
    public required BroAction Action { get; init; }
    public required IReadOnlyList<string> Inputs { get; init; }
    public CancellationTokenSource Cancel { get; } = new();

    public JobState State { get; private set; } = JobState.Queued;
    public double Progress { get; private set; }
    public IReadOnlyList<string> Outputs { get; private set; } = [];
    public string? Error { get; private set; }
    /// <summary>Optional one-liner for the toast, e.g. "Copied 214 words to the clipboard".</summary>
    public string? Note { get; internal set; }

    public event Action<Job>? Changed;

    public string Title => Inputs.Count == 1
        ? $"{Path.GetFileName(Inputs[0])}  →  {Action.Short}"
        : $"{Inputs.Count} files  →  {Action.Short}";

    internal void Set(JobState state, double? progress = null, IReadOnlyList<string>? outputs = null, string? error = null)
    {
        State = state;
        if (progress is { } p) Progress = p;
        if (outputs != null) Outputs = outputs;
        if (error != null) Error = error;
        Changed?.Invoke(this);
    }

    internal void ReportProgress(double value)
    {
        // Avoid flooding the UI: only report visible steps.
        if (value - Progress < 0.01 && value < 1) return;
        Progress = value;
        Changed?.Invoke(this);
    }
}

/// <summary>Lets a converter leave a note on the job it's running in.</summary>
public static class JobNotes
{
    internal static readonly AsyncLocal<Job?> Current = new();

    public static void Say(string note)
    {
        if (Current.Value is { } job) job.Note = note;
    }
}

public static class JobRunner
{
    // Two jobs at once keeps the machine responsive during big video jobs.
    static readonly SemaphoreSlim Slots = new(2);
    static int _active;

    public static event Action<Job>? JobStarted;
    public static event Action<Job>? JobFinished;

    public static int ActiveCount => Volatile.Read(ref _active);

    public static Job? Start(BroAction action, IReadOnlyList<string> files)
    {
        var inputs = files.Where(action.Accepts).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (inputs.Count < action.MinFiles) return null;

        var job = new Job { Action = action, Inputs = inputs };
        Interlocked.Increment(ref _active);
        JobStarted?.Invoke(job);
        _ = RunAsync(job);
        return job;
    }

    static async Task RunAsync(Job job)
    {
        try
        {
            await Slots.WaitAsync(job.Cancel.Token);
            try
            {
                job.Set(JobState.Running, 0);
                JobNotes.Current.Value = job;
                Log.Info($"Running {job.Action.Id} on {string.Join(", ", job.Inputs)}");
                var progress = new ProgressSink(job.ReportProgress);
                var outputs = await job.Action.Run(job.Inputs, progress, job.Cancel.Token);
                job.Set(JobState.Done, 1, outputs);
            }
            finally
            {
                Slots.Release();
            }
        }
        catch (OperationCanceledException)
        {
            job.Set(JobState.Canceled);
        }
        catch (Exception ex)
        {
            Log.Error($"{job.Action.Id} failed", ex);
            job.Set(JobState.Failed, error: Friendly(ex));
        }
        finally
        {
            Interlocked.Decrement(ref _active);
            JobFinished?.Invoke(job);
        }
    }

    static string Friendly(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Windows won't let me write in that folder. Try a file from Documents or Desktop.",
        IOException io when io.Message.Contains("being used") => "That file is open in another app. Close it and try again.",
        _ => ex.Message,
    };

    sealed class ProgressSink(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
