using CloudlogHelper.Utils;

namespace CloudlogHelper.Tests;

public class IpAddrParseTests
{
    [Theory]
    [InlineData("192.168.1.1:8080", "192.168.1.1", 8080)]
    [InlineData("127.0.0.1:3000", "127.0.0.1", 3000)]
    [InlineData("10.0.0.1:80", "10.0.0.1", 80)]
    [InlineData("255.255.255.255:65534", "255.255.255.255", 65534)]
    [InlineData("0.0.0.0:1", "0.0.0.0", 1)]
    public void ParseAddress_ValidAddress_ReturnsCorrectTuple(string input, string expectedIp, int expectedPort)
    {
        // Act
        var result = IPAddrUtil.ParseAddress(input);

        // Assert
        Assert.Equal(expectedIp, result.Item1);
        Assert.Equal(expectedPort, result.Item2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseAddress_NullOrEmptyAddress_ThrowsArgumentException(string input)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => IPAddrUtil.ParseAddress(input));
    }

    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData(":8080")]
    [InlineData("192.168.1.1:")]
    [InlineData("192.168.1.1:8080:extra")]
    public void ParseAddress_InvalidFormat_ThrowsArgumentException(string input)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => IPAddrUtil.ParseAddress(input));
    }

    [Theory]
    [InlineData("192.168.1.1:abc")]
    [InlineData("192.168.1.1:12.34")]
    [InlineData("192.168.1.1:-8080")]
    [InlineData("192.168.1.1:99999")]
    public void ParseAddress_InvalidPortNumber_ThrowsArgumentException(string input)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => IPAddrUtil.ParseAddress(input));
    }

    [Theory]
    [InlineData("192.168.1.1:0")]
    [InlineData("192.168.1.1:65535")]
    public void ParseAddress_PortOutOfRange_ThrowsArgumentException(string input)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => IPAddrUtil.ParseAddress(input));
    }

    [Theory]
    [InlineData("256.1.1.1:8080")]
    [InlineData("192.168.1.256:8080")]
    [InlineData("invalid.ip:8080")]
    [InlineData("192.168.1.1.1:8080")]
    public void ParseAddress_InvalidIpAddress_ThrowsArgumentException(string input)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => IPAddrUtil.ParseAddress(input));
    }

    [Theory]
    [InlineData("192.168.1.1:1", 1)]
    [InlineData("192.168.1.1:65534", 65534)]
    [InlineData("192.168.1.1:1024", 1024)]
    public void ParseAddress_ValidPortBoundaries_ReturnsCorrectPort(string input, int expectedPort)
    {
        // Act
        var result = IPAddrUtil.ParseAddress(input);

        // Assert
        Assert.Equal(expectedPort, result.Item2);
    }
}