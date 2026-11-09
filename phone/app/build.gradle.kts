plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "app.shiki.phone"
    compileSdk = 35

    defaultConfig {
        applicationId = "app.shiki.phone"
        minSdk = 29
        targetSdk = 35
        versionCode = 1
        versionName = "1.0"
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            // Installed by hand (from the PC), not from a store: the debug key is enough to sign it.
            signingConfig = signingConfigs.getByName("debug")
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
    lint {
        // The link trusts the PC by its pinned certificate fingerprint, not a certificate authority.
        disable += "TrustAllX509TrustManager"
        abortOnError = false
        checkReleaseBuilds = false
    }
}
