using BPGit.Data.Connection;
using Xunit;

namespace BPGit.Data.Tests;

public class ConnectionFactoryTests
{
    [Fact]
    public void Constructor_StoresConnectionString()
    {
        var factory = new ConnectionFactory("Server=mysrv;Database=bpdb;");
        Assert.Equal("Server=mysrv;Database=bpdb;", factory.ConnectionString);
    }

    [Fact]
    public void Create_ReturnsSqlConnection_WithProvidedConnectionString()
    {
        const string cs = "Server=localhost;Database=BluePrism;Integrated Security=SSPI;";
        var factory = new ConnectionFactory(cs);

        using var conn = factory.Create();

        Assert.Equal(cs, conn.ConnectionString);
    }

    [Fact]
    public void Create_NewInstanceEachCall()
    {
        var factory = new ConnectionFactory("Server=x;Database=y;");
        using var conn1 = factory.Create();
        using var conn2 = factory.Create();
        Assert.NotSame(conn1, conn2);
    }
}