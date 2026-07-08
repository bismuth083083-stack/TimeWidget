using System.Diagnostics;
using Windows.Devices.Geolocation;

namespace TimeWidget.Services;

public sealed record LocationResult(
    bool IsAllowed,
    double Latitude,
    double Longitude,
    string? Message);

public sealed class LocationService
{
    public async Task<LocationResult> GetCurrentLocationAsync()
    {
        try
        {
            GeolocationAccessStatus accessStatus = await Geolocator.RequestAccessAsync();
            if (accessStatus != GeolocationAccessStatus.Allowed)
            {
                return new LocationResult(
                    false,
                    0,
                    0,
                    "Location permission is off. You can choose a city manually.");
            }

            Geolocator geolocator = new()
            {
                DesiredAccuracyInMeters = 5000
            };

            Geoposition position = await geolocator.GetGeopositionAsync(
                maximumAge: TimeSpan.FromMinutes(15),
                timeout: TimeSpan.FromSeconds(10));

            return new LocationResult(
                true,
                position.Coordinate.Point.Position.Latitude,
                position.Coordinate.Point.Position.Longitude,
                null);
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"Location access denied: {ex}");
            return new LocationResult(false, 0, 0, "Windows location service is off.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Location request failed: {ex}");
            return new LocationResult(false, 0, 0, "Unable to get location. You can choose a city manually.");
        }
    }
}
