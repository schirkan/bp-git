using Microsoft.Data.SqlClient;

namespace BPGit.Data.Connection;

public class ConnectionFactory
{
    public string ConnectionString { get; }

    public ConnectionFactory(string connectionString)
    {
        ConnectionString = connectionString;
    }

    public SqlConnection Create() => new(ConnectionString);
}
