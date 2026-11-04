using Amazon.SimpleSystemsManagement.Model;
using RouteGo.App.Infrastructure;

namespace RouteGo.App.Tests.Infrastructure;

public sealed class FlatKeyParameterProcessorTests
{
    private const string ParameterPath = "/routego/prod/portal/";

    [Fact]
    public void GetKey_KebabCaseName_ReturnsPascalCaseKey()
    {
        // Arrange
        var processor = new FlatKeyParameterProcessor();
        var parameter = new Parameter { Name = "/routego/prod/portal/cdn-base-url" };

        // Act
        string key = processor.GetKey(parameter, ParameterPath);

        // Assert
        Assert.Equal("CdnBaseUrl", key);
    }

    [Fact]
    public void GetKey_SingleWordName_ReturnsCapitalisedKey()
    {
        // Arrange
        var processor = new FlatKeyParameterProcessor();
        var parameter = new Parameter { Name = "/routego/prod/portal/tickets" };

        // Act
        string key = processor.GetKey(parameter, ParameterPath);

        // Assert
        Assert.Equal("Tickets", key);
    }
}
