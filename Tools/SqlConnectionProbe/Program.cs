using Microsoft.Data.SqlClient;

var candidates = new[]
{
    @"Data Source=LAPTOP-L7SR38HF\SQLEXPRESS;Database=LOL;Integrated Security=True;Pooling=False;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5;",
    @"Data Source=LAPTOP-L7SR38HF\SQLEXPRESS;Database=LOL;Integrated Security=True;Pooling=False;Encrypt=False;TrustServerCertificate=True;Connect Timeout=5;",
    @"Data Source=localhost\SQLEXPRESS;Database=LOL;Integrated Security=True;Pooling=False;Encrypt=False;TrustServerCertificate=True;Connect Timeout=5;",
    @"Data Source=.\SQLEXPRESS;Database=LOL;Integrated Security=True;Pooling=False;Encrypt=False;TrustServerCertificate=True;Connect Timeout=5;",
    @"Data Source=lpc:(local)\SQLEXPRESS;Database=LOL;Integrated Security=True;Pooling=False;Encrypt=False;TrustServerCertificate=True;Connect Timeout=5;",
    @"Data Source=lpc:localhost\SQLEXPRESS;Database=LOL;Integrated Security=True;Pooling=False;Encrypt=False;TrustServerCertificate=True;Connect Timeout=5;",
    @"Data Source=np:\\.\pipe\MSSQL$SQLEXPRESS\sql\query;Database=LOL;Integrated Security=True;Pooling=False;Encrypt=False;TrustServerCertificate=True;Connect Timeout=5;",
    @"Data Source=tcp:127.0.0.1,1433;Database=LOL;Integrated Security=True;Pooling=False;Encrypt=False;TrustServerCertificate=True;Connect Timeout=5;",
    @"Data Source=tcp:localhost,1433;Database=LOL;Integrated Security=True;Pooling=False;Encrypt=False;TrustServerCertificate=True;Connect Timeout=5;",
    @"Data Source=tcp:LAPTOP-L7SR38HF,1433;Database=LOL;Integrated Security=True;Pooling=False;Encrypt=False;TrustServerCertificate=True;Connect Timeout=5;",
    @"Data Source=localhost\SQLEXPRESS;Database=LOL;Integrated Security=True;Pooling=False;Encrypt=False;TrustServerCertificate=True;Server SPN=MSSQLSvc/localhost:SQLEXPRESS;Connect Timeout=5;",
    @"Data Source=.\SQLEXPRESS;Database=LOL;Integrated Security=True;Pooling=False;Encrypt=False;TrustServerCertificate=True;Server SPN=MSSQLSvc/LAPTOP-L7SR38HF:SQLEXPRESS;Connect Timeout=5;",
    @"Data Source=lpc:(local)\SQLEXPRESS;Database=LOL;Integrated Security=True;Pooling=False;Encrypt=False;TrustServerCertificate=True;Server SPN=MSSQLSvc/LAPTOP-L7SR38HF:SQLEXPRESS;Connect Timeout=5;",
    @"Data Source=localhost\SQLEXPRESS;Database=LOL;User ID=__invalid_probe__;Password=<invalid-probe-password>;Pooling=False;Encrypt=False;TrustServerCertificate=True;Connect Timeout=5;"
};

foreach (var connectionString in candidates)
{
    var builder = new SqlConnectionStringBuilder(connectionString);
    Console.Write($"DataSource={builder.DataSource}; Encrypt={builder.Encrypt}; Integrated={builder.IntegratedSecurity}; UserID={(string.IsNullOrWhiteSpace(builder.UserID) ? "(none)" : builder.UserID)} ... ");

    try
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT DB_NAME(), SUSER_SNAME(), @@SERVERNAME", connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        Console.WriteLine($"OK db={reader.GetString(0)} login={reader.GetString(1)} server={reader.GetString(2)}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL {ex.GetType().Name}: {ex.Message.Split(Environment.NewLine)[0]}");
    }
}
