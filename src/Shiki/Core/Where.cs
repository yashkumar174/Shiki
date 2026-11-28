using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Shiki.Core;

/// <summary>
/// Where the person is, as a town: from Windows' own location (the one Weather and Maps use),
/// matched to the nearest city in a list that ships with Shiki, so the position itself never
/// leaves the PC (only the town's name goes to Claude). The person can set it by hand too.
/// Cities: GeoNames (geonames.org, CC BY 4.0), every place of 15,000 people or more.
/// </summary>
public static class Where
{
    public sealed record Place(string City, string Region, string Country, double Latitude, double Longitude, bool Typed, double Metres = 0)
    {
        public override string ToString() => string.Join(", ", new[] { City, Region, Country }.Where(p => p.Length > 0).Distinct());
    }

    static Place? _found;
    static DateTime _foundAt;
    static int _looking;

    /// <summary>The town set by hand in Settings, or the one Windows' location points to (looked up now and then).</summary>
    public static Place? Now(Settings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.HomeCity)) return new Place(settings.HomeCity.Trim(), "", "", 0, 0, true);
        if (!settings.UseLocation) return null;
        // Look again every few hours (a laptop moves); the old answer stands meanwhile.
        if (DateTime.Now - _foundAt > TimeSpan.FromHours(3)) _ = Refresh();
        return _found;
    }

    /// <summary>A line for the cat's context: where the person is, or that it isn't known.</summary>
    public static string Note(Settings settings) => Now(settings) is { } place
        ? place.Typed
            ? $" The person lives in {place} (set by them)."
            : place.Metres > 3000
                ? $" Windows' rough location (from the network, could be off by tens of km or a whole city) puts the PC around {place}; use it as the starting point for travel, weather and \"near me\" unless they say otherwise, and say which city you assumed."
                : $" The person is in or near {place} (from Windows' location); use it as the starting point for travel, weather and \"near me\" unless they say otherwise."
        : " Where the person is isn't known; when it matters (travel, weather, near me), ask once, then remember it.";

    /// <summary>Asks Windows where the PC is (quietly: no answer if location is off), and finds the town.</summary>
    public static async Task Refresh()
    {
        if (Interlocked.Exchange(ref _looking, 1) == 1) return;
        try
        {
            var access = await Windows.Devices.Geolocation.Geolocator.RequestAccessAsync();
            if (access != Windows.Devices.Geolocation.GeolocationAccessStatus.Allowed)
            {
                _foundAt = DateTime.Now;
                Log.Info($"Location: Windows says {access}");
                return;
            }
            var locator = new Windows.Devices.Geolocation.Geolocator { DesiredAccuracy = Windows.Devices.Geolocation.PositionAccuracy.Default };
            var position = await locator.GetGeopositionAsync(TimeSpan.FromHours(1), TimeSpan.FromSeconds(15));
            var point = position.Coordinate.Point.Position;
            _found = Nearest(point.Latitude, point.Longitude) is { } town ? town with { Metres = position.Coordinate.Accuracy } : null;
            _foundAt = DateTime.Now;
            if (_found != null) Log.Info($"Location: near {_found} (to within {_found.Metres / 1000:0.#} km)");
        }
        catch (Exception ex)
        {
            _foundAt = DateTime.Now;
            Log.Error("Finding where the PC is", ex);
        }
        finally { Interlocked.Exchange(ref _looking, 0); }
    }

    static List<Place>? _cities;

    /// <summary>The town for a point: the biggest place within 20 km (people say "Jamshedpur", not its suburb), else the nearest.</summary>
    public static Place? Nearest(double lat, double lon)
    {
        var cities = Cities();
        if (cities.Count == 0) return null;
        Place? best = null, nearest = null;
        double bestPeople = -1, nearestKm = double.MaxValue;
        foreach (var c in cities)
        {
            double km = Km(lat, lon, c.Latitude, c.Longitude);
            if (km < nearestKm) { nearestKm = km; nearest = c; }
            if (km <= 20 && People[c] > bestPeople) { bestPeople = People[c]; best = c; }
        }
        return best ?? nearest;
    }

    static readonly Dictionary<Place, long> People = new(ReferenceEqualityComparer.Instance);

    static List<Place> Cities()
    {
        if (_cities != null) return _cities;
        var list = new List<Place>();
        try
        {
            var file = Path.Combine(AppContext.BaseDirectory, "Assets", "cities.tsv.gz");
            using var reader = new StreamReader(new GZipStream(File.OpenRead(file), CompressionMode.Decompress));
            while (reader.ReadLine() is { } line)
            {
                var p = line.Split('\t');
                if (p.Length < 6) continue;
                var place = new Place(p[0], p[1], p[2], double.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture), double.Parse(p[4], System.Globalization.CultureInfo.InvariantCulture), false);
                People[place] = long.TryParse(p[5], out var n) ? n : 0;
                list.Add(place);
            }
        }
        catch (Exception ex) { Log.Error("Reading the list of cities", ex); }
        return _cities = list;
    }

    static double Km(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371;
        double dLat = (lat2 - lat1) * Math.PI / 180, dLon = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Asin(Math.Sqrt(a));
    }
}
