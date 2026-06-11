using System.Reflection;
using Proposal.Services;

namespace Proposal.Tests;

public sealed class EquipmentRepositoryQueryTests
{
    [Theory]
    [InlineData(typeof(SqlEquipmentRepository), "@User", "ORDER BY")]
    [InlineData(typeof(PostgresEquipmentRepository), "@username", "order by")]
    public void ListQuery_SeparatesUsernameParameterFromOrderClause(
        Type repositoryType,
        string usernameParameter,
        string orderClause)
    {
        var method = repositoryType.GetMethod(
            "BuildListSql",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var sql = Assert.IsType<string>(method.Invoke(null, [null, null]));

        Assert.Contains(usernameParameter, sql, StringComparison.Ordinal);
        Assert.Contains(orderClause, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Matches($"{RegexEscape(usernameParameter)}\\s+{RegexEscape(orderClause)}", sql);
    }

    private static string RegexEscape(string value) =>
        System.Text.RegularExpressions.Regex.Escape(value);
}
