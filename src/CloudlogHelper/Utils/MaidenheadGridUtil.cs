using System;
using System.Text;

namespace CloudlogHelper.Utils;

public class LatLng
{
    public LatLng(double lat, double lng)
    {
        Latitude = lat;
        Longitude = lng;
    }

    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

/// <summary>
///     This code references [com.bg7yoz.ft8cn.maidenhead] from FT8CN.
/// </summary>
public static class MaidenheadGridUtil
{
    private const double EARTH_RADIUS = 6371393; // 平均半径,单位：m；不是赤道半径。赤道为6378左右

    /// <summary>
    ///     计算梅登海德网格的经纬度，4字符或6字符。如果网格数据不正确，返回null。如果是四字符的，尾部加ll,取中间的位置。
    /// </summary>
    /// <param name="grid">梅登海德网格数据</param>
    /// <returns>返回经纬度，如果数据不正确，返回null</returns>
    public static LatLng? GridToLatLng(string? grid)
    {
        if (grid is null) return null;
        if (grid.Length == 0) return null;

        //判断是不是符合梅登海德网格的规则
        if (grid.Length != 2 && grid.Length != 4 && grid.Length != 6) return null;
        if (grid.Equals("RR73", StringComparison.OrdinalIgnoreCase)) return null;
        if (grid.Equals("RR", StringComparison.OrdinalIgnoreCase)) return null;

        double x = 0;
        double y = 0;
        double z = 0;

        //纬度
        double lat = 0;
        if (grid.Length == 2)
            x = grid.ToUpper()[1] - 'A' + 0.5f;
        else
            x = grid.ToUpper()[1] - 'A';
        x *= 10;

        if (grid.Length == 4)
            y = grid[3] - '0' + 0.5f;
        else if (grid.Length == 6) y = grid[3] - '0';

        if (grid.Length == 6)
        {
            z = grid.ToUpper()[5] - 'A' + 0.5f;
            z = z * (1 / 18f);
        }

        lat = x + y + z - 90;

        //经度
        x = 0;
        y = 0;
        z = 0;
        double lng = 0;
        if (grid.Length == 2)
            x = grid.ToUpper()[0] - 'A' + 0.5;
        else
            x = grid.ToUpper()[0] - 'A';
        x *= 20;

        if (grid.Length == 4)
            y = grid[2] - '0' + 0.5;
        else if (grid.Length == 6) y = grid[2] - '0';
        y *= 2;

        if (grid.Length == 6)
        {
            z = grid.ToUpper()[4] - 'A' + 0.5;
            z = z * (2 / 18f);
        }

        lng = x + y + z - 180;

        if (lat > 85) lat = 85; //防止在地图上越界
        if (lat < -85) lat = -85; //防止在地图上越界

        return new LatLng(lat, lng);
    }

    public static LatLng[] GridToPolygon(string grid)
    {
        if (grid.Length != 2 && grid.Length != 4 && grid.Length != 6) return null;

        var latLngs = new LatLng[4];

        //纬度1
        double x;
        double y = 0;
        double z = 0;
        double lat1;
        x = grid.ToUpper()[1] - 'A';
        x *= 10;

        if (grid.Length > 2) y = grid[3] - '0';

        if (grid.Length > 4)
        {
            z = grid.ToUpper()[5] - 'A';
            z = z * (1f / 18f);
        }

        lat1 = x + y + z - 90;

        if (lat1 < -85.0) lat1 = -85.0;
        if (lat1 > 85.0) lat1 = 85.0;

        //纬度2
        x = 0;
        y = 0;
        z = 0;
        double lat2;

        if (grid.Length == 2)
            x = grid.ToUpper()[1] - 'A' + 1;
        else
            x = grid.ToUpper()[1] - 'A';
        x *= 10;

        if (grid.Length == 4)
            y = grid[3] - '0' + 1;
        else if (grid.Length == 6) y = grid[3] - '0';

        if (grid.Length == 6)
        {
            z = grid.ToUpper()[5] - 'A' + 1;
            z = z * (1f / 18f);
        }

        lat2 = x + y + z - 90;

        if (lat2 < -85.0) lat2 = -85.0;
        if (lat2 > 85.0) lat2 = 85.0;

        //经度1
        x = 0;
        y = 0;
        z = 0;
        double lng1;
        x = grid.ToUpper()[0] - 'A';
        x *= 20;

        if (grid.Length > 2)
        {
            y = grid[2] - '0';
            y *= 2;
        }

        if (grid.Length > 4)
        {
            z = grid.ToUpper()[4] - 'A';
            z = z * 2 / 18f;
        }

        lng1 = x + y + z - 180;

        //经度2
        x = 0;
        y = 0;
        z = 0;
        double lng2;

        if (grid.Length == 2)
            x = grid.ToUpper()[0] - 'A' + 1;
        else
            x = grid.ToUpper()[0] - 'A';
        x *= 20;

        if (grid.Length == 4)
            y = grid[2] - '0' + 1;
        else if (grid.Length == 6) y = grid[2] - '0';
        y *= 2;

        if (grid.Length == 6)
        {
            z = grid.ToUpper()[4] - 'A' + 1;
            z = z * 2 / 18f;
        }

        lng2 = x + y + z - 180;

        latLngs[0] = new LatLng(lat1, lng1);
        latLngs[1] = new LatLng(lat1, lng2);
        latLngs[2] = new LatLng(lat2, lng2);
        latLngs[3] = new LatLng(lat2, lng1);

        return latLngs;
    }

    /// <summary>
    ///     此函数根据纬度计算 6 字符 Maidenhead网格。
    ///     经纬度采用 NMEA 格式。换句话说，西经和南纬度为负数。它们被指定为double类型
    /// </summary>
    /// <param name="location">经纬度</param>
    /// <returns>梅登海德字符</returns>
    public static string GetGridSquare(LatLng location)
    {
        var _long = location.Longitude;
        var _lat = location.Latitude;
        var buff = new StringBuilder();

        /*
         *	计算第一对两个字符
         */
        _long += 180; // 从太平洋中部开始
        var tempNumber = _long / 20; // 每个主要正方形都是 20 度宽
        //用于中间计算
        var index = (int)tempNumber; // 大写字母的索引
        //确定要显示的字符
        buff.Append((char)(index + 'A')); // 设置第一个字符
        _long = _long - index * 20; // 第 2 步的剩余部分

        _lat += 90; //从南极开始 180 度
        tempNumber = _lat / 10; // 每个大正方形高 10 度
        index = (int)tempNumber; // 大写字母的索引
        buff.Append((char)(index + 'A')); //设置第二个字符
        _lat = _lat - index * 10; // 第 2 步的剩余部分

        /*
         *	现在是第二对两数字：
         */
        tempNumber = _long / 2; // 步骤 1 的余数除以 2
        index = (int)tempNumber; // 数字索引
        buff.Append((char)(index + '0')); //设置第三个字符
        _long = _long - index * 2; //第 3 步的剩余部分

        tempNumber = _lat; // 步骤 1 的余数除以 1
        index = (int)tempNumber; // 数字索引
        buff.Append((char)(index + '0')); //设置第四个字符
        _lat = _lat - index; //第 3 步的剩余部分

        /*
         *现在是第三对两个小写字符：
         */
        tempNumber = _long / 0.083333; //步骤 2 的余数除以 0.083333
        index = (int)tempNumber; // 小写字母的索引
        buff.Append((char)(index + 'a')); //设置第五个字符

        tempNumber = _lat / 0.0416665; // 步骤 2 的余数除以 0.0416665
        index = (int)tempNumber; // 小写字母的索引
        buff.Append((char)(index + 'a')); //设置第五个字符

        return buff.ToString().Substring(0, 4);
    }

    /// <summary>
    ///     计算经纬度之间的距离
    /// </summary>
    /// <param name="latLng1">经纬度</param>
    /// <param name="latLng2">经纬度</param>
    /// <returns>距离，公里。</returns>
    public static double GetDist(LatLng? latLng1, LatLng? latLng2)
    {
        var radiansAX = ToRadians(latLng1.Longitude); // A经弧度
        var radiansAY = ToRadians(latLng1.Latitude); // A纬弧度
        var radiansBX = ToRadians(latLng2.Longitude); // B经弧度
        var radiansBY = ToRadians(latLng2.Latitude); // B纬弧度

        // 公式中"cosβ1cosβ2cos（α1-α2）+sinβ1sinβ2"的部分，得到∠AOB的cos值
        var cos = Math.Cos(radiansAY) * Math.Cos(radiansBY) * Math.Cos(radiansAX - radiansBX)
                  + Math.Sin(radiansAY) * Math.Sin(radiansBY);
        var acos = Math.Acos(cos); // 反余弦值
        return EARTH_RADIUS * acos / 1000; // 最终结果km
    }

    private static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180;
    }

    /// <summary>
    ///     计算梅登海德网格之间的距离
    /// </summary>
    /// <param name="mGrid1">梅登海德网格</param>
    /// <param name="mGrid2">梅登海德网格2</param>
    /// <returns>两个网格之间的距离</returns>
    public static double GetDist(string? mGrid1, string? mGrid2)
    {
        if (mGrid1 == mGrid2) return 0;
        var latLng1 = GridToLatLng(mGrid1);
        var latLng2 = GridToLatLng(mGrid2);
        if (latLng1 != null && latLng2 != null) return GetDist(latLng1, latLng2);

        return -1;
    }

    /// <summary>
    ///     计算两个网格之间的距离
    /// </summary>
    /// <param name="mGrid1">网格</param>
    /// <param name="mGrid2">网格</param>
    /// <returns>距离</returns>
    public static string GetDistStr(string? mGrid1, string? mGrid2)
    {
        var dist = GetDist(mGrid1, mGrid2);
        if (dist == 0) return "";

        return string.Format("{0:F0} km", dist);
    }

    public static string GetDistLatLngStr(LatLng? latLng1, LatLng? latLng2)
    {
        return string.Format("{0:F0} km", GetDist(latLng1, latLng2));
    }

    /// <summary>
    ///     计算两个网格之间的距离，以英文显示公里数
    /// </summary>
    /// <param name="mGrid1">网格</param>
    /// <param name="mGrid2">网格</param>
    /// <returns>距离</returns>
    public static string GetDistStrEN(string? mGrid1, string? mGrid2)
    {
        var dist = GetDist(mGrid1, mGrid2);
        if (dist == 0) return "";

        return string.Format("{0:F0} km", dist);
    }

    /// <summary>
    ///     检查是不是梅登海德网格。如果不是返回false。
    /// </summary>
    /// <param name="s">梅登海德网格</param>
    /// <returns>是否是梅登海德网格。</returns>
    public static bool CheckMaidenhead(string? s)
    {
        if (s is null) return false;
        if (s.Length != 4 && s.Length != 6) return false;

        if (s.Equals("RR73", StringComparison.OrdinalIgnoreCase)) return false;
        return char.IsLetter(s[0])
               && char.IsLetter(s[1])
               && char.IsDigit(s[2])
               && char.IsDigit(s[3]);
    }

    // 计算经纬度间航向角
    public static double CalculateBearing(double lon1, double lat1, double lon2, double lat2)
    {
        var lat1Rad = lat1 * Math.PI / 180.0;
        var lon1Rad = lon1 * Math.PI / 180.0;
        var lat2Rad = lat2 * Math.PI / 180.0;
        var lon2Rad = lon2 * Math.PI / 180.0;

        var deltaLon = lon2Rad - lon1Rad;

        var y = Math.Sin(deltaLon) * Math.Cos(lat2Rad);
        var x = Math.Cos(lat1Rad) * Math.Sin(lat2Rad) -
                Math.Sin(lat1Rad) * Math.Cos(lat2Rad) * Math.Cos(deltaLon);

        var theta = Math.Atan2(y, x) * 180.0 / Math.PI;

        // 确保结果在 0-360 范围内
        return (theta + 360) % 360;
    }

    // 计算网格间航向角
    public static double CalculateBearing(string grid1, string grid2)
    {
        if (grid1 == grid2) return 0;
        var gridToLatLng1 = GridToLatLng(grid1);
        var gridToLatLng2 = GridToLatLng(grid2);
        return CalculateBearing(gridToLatLng1!.Longitude, gridToLatLng1.Latitude,
            gridToLatLng2!.Longitude, gridToLatLng2.Latitude);
    }
}