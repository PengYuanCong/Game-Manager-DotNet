using System.Data;
using Microsoft.Data.SqlClient;
using Proposal.Models;

namespace Proposal.Services;

public sealed class SqlCalculatorDataRepository : ICalculatorDataRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public SqlCalculatorDataRepository(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<Equipment>> GetEquipmentOptionsAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        var equipments = new List<Equipment>();

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT Id, Name
            FROM dbo.Equipments
            WHERE Username = @User
            ORDER BY Price ASC, Name ASC;
            """;

        await using var command = new SqlCommand(sql, connection);
        AddUsername(command, username);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            equipments.Add(new Equipment
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Name = ReadString(reader, "Name", string.Empty)
            });
        }

        return equipments;
    }

    public async Task<IReadOnlyList<Loadout>> GetLoadoutOptionsAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        var loadouts = new List<Loadout>();

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT l.Id, l.LoadoutName
            FROM dbo.Loadouts l
            WHERE l.Username = @User
              AND EXISTS (
                    SELECT 1
                    FROM (VALUES (l.Eq1_Id), (l.Eq2_Id), (l.Eq3_Id), (l.Eq4_Id), (l.Eq5_Id), (l.Eq6_Id)) v(EquipmentId)
                    INNER JOIN dbo.Equipments e ON e.Id = v.EquipmentId AND e.Username = l.Username
                    WHERE v.EquipmentId IS NOT NULL
              )
            ORDER BY ISNULL(l.CreatedAt, '19000101') DESC, l.Id DESC;
            """;

        await using var command = new SqlCommand(sql, connection);
        AddUsername(command, username);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            loadouts.Add(new Loadout
            {
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                LoadoutName = ReadString(reader, "LoadoutName", string.Empty)
            });
        }

        return loadouts;
    }

    public async Task<LoadoutStats?> GetLoadoutStatsAsync(
        string username,
        int loadoutId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                ISNULL(SUM(e.HP), 0) as TotalHP,
                ISNULL(SUM(e.Mana), 0) as TotalMana,
                ISNULL(SUM(e.Attack), 0) as TotalAtk,
                ISNULL(SUM(e.MagicAttack), 0) as TotalMAtk,
                ISNULL(SUM(e.PhysicalDefense), 0) as TotalPDef,
                ISNULL(SUM(e.MagicDefense), 0) as TotalMDef,
                ISNULL(SUM(e.HealthRegen), 0) as TotalHealthRegen,
                ISNULL(SUM(e.ManaRegen), 0) as TotalManaRegen,
                ISNULL(SUM(e.AbilityHaste), 0) as TotalAbilityHaste,
                ISNULL(SUM(e.AttackSpeed), 0) as TotalAttackSpeed,
                ISNULL(SUM(e.CriticalStrikeChance), 0) as TotalCrit,
                ISNULL(SUM(e.MoveSpeed), 0) as TotalMoveSpeed,
                ISNULL(SUM(e.MoveSpeedPercent), 0) as TotalMoveSpeedPercent,
                ISNULL(SUM(e.Lethality), 0) as TotalLethality,
                ISNULL(SUM(e.ArmorPenetrationPercent), 0) as TotalArmorPenetrationPercent,
                ISNULL(SUM(e.MagicPenetration), 0) as TotalMagicPenetration,
                ISNULL(SUM(e.MagicPenetrationPercent), 0) as TotalMagicPenetrationPercent,
                ISNULL(SUM(e.LifeSteal), 0) as TotalLifeSteal,
                ISNULL(SUM(e.Omnivamp), 0) as TotalOmnivamp,
                ISNULL(SUM(e.HealAndShieldPower), 0) as TotalHealAndShieldPower,
                ISNULL(SUM(e.Tenacity), 0) as TotalTenacity,
                ISNULL(SUM(e.Price), 0) as TotalPrice
            FROM dbo.Loadouts l
            CROSS APPLY (
                SELECT HP, Mana, Attack, MagicAttack, PhysicalDefense, MagicDefense,
                       HealthRegen, ManaRegen, AbilityHaste, AttackSpeed, CriticalStrikeChance,
                       MoveSpeed, MoveSpeedPercent, Lethality, ArmorPenetrationPercent,
                       MagicPenetration, MagicPenetrationPercent, LifeSteal, Omnivamp,
                       HealAndShieldPower, Tenacity, Price
                FROM dbo.Equipments
                WHERE Id IN (l.Eq1_Id, l.Eq2_Id, l.Eq3_Id, l.Eq4_Id, l.Eq5_Id, l.Eq6_Id)
                  AND Username = @User
            ) e
            WHERE l.Id = @LoadoutId AND l.Username = @User;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@LoadoutId", SqlDbType.Int).Value = loadoutId;
        AddUsername(command, username);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new LoadoutStats
        {
            Hp = ReadInt32(reader, "TotalHP"),
            Mana = ReadInt32(reader, "TotalMana"),
            Attack = ReadInt32(reader, "TotalAtk"),
            MagicAttack = ReadInt32(reader, "TotalMAtk"),
            PDef = ReadInt32(reader, "TotalPDef"),
            MDef = ReadInt32(reader, "TotalMDef"),
            HealthRegen = ReadDecimal(reader, "TotalHealthRegen"),
            ManaRegen = ReadDecimal(reader, "TotalManaRegen"),
            AbilityHaste = ReadDecimal(reader, "TotalAbilityHaste"),
            AttackSpeed = ReadDecimal(reader, "TotalAttackSpeed"),
            CriticalStrikeChance = ReadDecimal(reader, "TotalCrit"),
            MoveSpeed = ReadInt32(reader, "TotalMoveSpeed"),
            MoveSpeedPercent = ReadDecimal(reader, "TotalMoveSpeedPercent"),
            Lethality = ReadDecimal(reader, "TotalLethality"),
            ArmorPenetrationPercent = ReadDecimal(reader, "TotalArmorPenetrationPercent"),
            MagicPenetration = ReadDecimal(reader, "TotalMagicPenetration"),
            MagicPenetrationPercent = ReadDecimal(reader, "TotalMagicPenetrationPercent"),
            LifeSteal = ReadDecimal(reader, "TotalLifeSteal"),
            Omnivamp = ReadDecimal(reader, "TotalOmnivamp"),
            HealAndShieldPower = ReadDecimal(reader, "TotalHealAndShieldPower"),
            Tenacity = ReadDecimal(reader, "TotalTenacity"),
            Price = ReadInt32(reader, "TotalPrice")
        };
    }


    private static void AddUsername(SqlCommand command, string username)
    {
        command.Parameters.Add("@User", SqlDbType.NVarChar, 100).Value = username;
    }

    private static string ReadString(SqlDataReader reader, string name, string fallback)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? fallback : reader.GetString(ordinal);
    }

    private static int ReadInt32(SqlDataReader reader, string name)
    {
        var value = reader[name];
        return value == DBNull.Value ? 0 : Convert.ToInt32(value);
    }

    private static decimal ReadDecimal(SqlDataReader reader, string name)
    {
        var value = reader[name];
        return value == DBNull.Value ? 0 : Convert.ToDecimal(value);
    }
}

