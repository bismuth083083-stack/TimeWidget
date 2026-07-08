using TimeWidget.Models;

namespace TimeWidget.Services;

public static class ChinaWeatherCityCatalog
{
    private static readonly List<WeatherCityInfo> Cities =
    [
        new("Beijing", "101010100", 39.9042, 116.4074),
        new("Shanghai", "101020100", 31.2304, 121.4737),
        new("Guangzhou", "101280101", 23.1291, 113.2644),
        new("Shenzhen", "101280601", 22.5431, 114.0579),
        new("Tianjin", "101030100", 39.3434, 117.3616),
        new("Chongqing", "101040100", 29.5630, 106.5516),
        new("Chengdu", "101270101", 30.5728, 104.0668),
        new("Hangzhou", "101210101", 30.2741, 120.1551),
        new("Nanjing", "101190101", 32.0603, 118.7969),
        new("Wuhan", "101200101", 30.5928, 114.3055),
        new("Xi'an", "101110101", 34.3416, 108.9398),
        new("Suzhou", "101190401", 31.2989, 120.5853),
        new("Qingdao", "101120201", 36.0671, 120.3826),
        new("Zhengzhou", "101180101", 34.7466, 113.6254),
        new("Changsha", "101250101", 28.2282, 112.9388),
        new("Shenyang", "101070101", 41.8057, 123.4315),
        new("Harbin", "101050101", 45.8038, 126.5349),
        new("Jinan", "101120101", 36.6512, 117.1201),
        new("Fuzhou", "101230101", 26.0745, 119.2965),
        new("Xiamen", "101230201", 24.4798, 118.0894),
        new("Kunming", "101290101", 25.0389, 102.7183),
        new("Nanning", "101300101", 22.8170, 108.3669),
        new("Haikou", "101310101", 20.0440, 110.1999),
        new("Sanya", "101310201", 18.2528, 109.5119),
        new("Guiyang", "101260101", 26.6470, 106.6302),
        new("Hefei", "101220101", 31.8206, 117.2272),
        new("Nanchang", "101240101", 28.6820, 115.8579),
        new("Taiyuan", "101100101", 37.8706, 112.5489),
        new("Shijiazhuang", "101090101", 38.0428, 114.5149),
        new("Hohhot", "101080101", 40.8426, 111.7492),
        new("Urumqi", "101130101", 43.8256, 87.6168),
        new("Lhasa", "101140101", 29.6520, 91.1721),
        new("Lanzhou", "101160101", 36.0611, 103.8343),
        new("Xining", "101150101", 36.6171, 101.7782),
        new("Yinchuan", "101170101", 38.4872, 106.2309),
        new("Hong Kong", "101320101", 22.3193, 114.1694),
        new("Macau", "101330101", 22.1987, 113.5439)
    ];

    public static IReadOnlyList<WeatherCityInfo> All => Cities;

    public static WeatherCityInfo DefaultCity => Cities[0];

    public static WeatherCityInfo FindByCode(string? code)
    {
        return Cities.FirstOrDefault(city => city.Code == code) ?? DefaultCity;
    }

    public static WeatherCityInfo FindNearest(double latitude, double longitude)
    {
        return Cities
            .OrderBy(city => GetDistanceSquared(latitude, longitude, city.Latitude, city.Longitude))
            .First();
    }

    private static double GetDistanceSquared(double latitude, double longitude, double otherLatitude, double otherLongitude)
    {
        double latitudeDelta = latitude - otherLatitude;
        double longitudeDelta = (longitude - otherLongitude) * Math.Cos(latitude * Math.PI / 180);
        return latitudeDelta * latitudeDelta + longitudeDelta * longitudeDelta;
    }
}
