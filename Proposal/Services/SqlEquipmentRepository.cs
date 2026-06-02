using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using Proposal.Models;

namespace Proposal.Services;

public sealed class SqlEquipmentRepository : IEquipmentRepository
{
    private const string EquipmentColumns = @"
        Id, Name, HP, Mana, Attack, MagicAttack, PhysicalDefense, MagicDefense,
        HealthRegen, ManaRegen, AbilityHaste, AttackSpeed, CriticalStrikeChance,
        MoveSpeed, MoveSpeedPercent, Lethality, ArmorPenetrationPercent,
        MagicPenetration, MagicPenetrationPercent, LifeSteal, Omnivamp,
        HealAndShieldPower, Tenacity, Price, DataDragonId, ItemImageUrl, ItemTags, ItemDescription";

    private readonly ISqlConnectionFactory _connectionFactory;

    public SqlEquipmentRepository(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<Equipment>> ListAsync(
        string username,
        string? searchString = null,
        string? statFilter = null,
        CancellationToken cancellationToken = default)
    {
        var equipments = new List<Equipment>();

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await EnsureEquipmentSchemaAsync(connection, cancellationToken);

        var sql = new StringBuilder($"""
            SELECT {EquipmentColumns}
            FROM dbo.Equipments
            WHERE Username = @User
            """);

        if (!string.IsNullOrWhiteSpace(searchString))
        {
            sql.AppendLine("AND Name LIKE @Search");
        }

        var statCondition = BuildStatFilterCondition(statFilter);
        if (!string.IsNullOrWhiteSpace(statCondition))
        {
            sql.AppendLine($"AND ({statCondition})");
        }

        sql.AppendLine("ORDER BY Price ASC, Name ASC;");

        await using var command = new SqlCommand(sql.ToString(), connection);
        AddUsername(command, username);
        if (!string.IsNullOrWhiteSpace(searchString))
        {
            command.Parameters.Add("@Search", SqlDbType.NVarChar, 200).Value = $"%{searchString.Trim()}%";
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            equipments.Add(ReadEquipment(reader));
        }

        return equipments;
    }

    public async Task<Equipment?> GetByIdAsync(
        string username,
        int id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await EnsureEquipmentSchemaAsync(connection, cancellationToken);

        var sql = $"SELECT {EquipmentColumns} FROM dbo.Equipments WHERE Id = @Id AND Username = @User;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        AddUsername(command, username);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEquipment(reader) : null;
    }

    public async Task CreateAsync(
        string username,
        Equipment equipment,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await EnsureEquipmentSchemaAsync(connection, cancellationToken);

        await using var command = new SqlCommand(InsertEquipmentSql, connection);
        AddUsername(command, username);
        AddEquipmentParameters(command, equipment);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CreateManyAsync(
        string username,
        IEnumerable<Equipment> equipments,
        CancellationToken cancellationToken = default)
    {
        var rows = equipments
            .Where(equipment => !string.IsNullOrWhiteSpace(equipment.Name))
            .ToList();
        if (rows.Count == 0)
        {
            return;
        }

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await EnsureEquipmentSchemaAsync(connection, cancellationToken);

        foreach (var equipment in rows)
        {
            await using var command = new SqlCommand(InsertEquipmentSql, connection);
            AddUsername(command, username);
            AddEquipmentParameters(command, equipment);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<bool> UpdateAsync(
        string username,
        Equipment equipment,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await EnsureEquipmentSchemaAsync(connection, cancellationToken);

        const string sql = """
            UPDATE dbo.Equipments
            SET Name = @Name,
                HP = @HP,
                Mana = @Mana,
                Attack = @Attack,
                MagicAttack = @MagicAttack,
                PhysicalDefense = @PhysicalDefense,
                MagicDefense = @MagicDefense,
                HealthRegen = @HealthRegen,
                ManaRegen = @ManaRegen,
                AbilityHaste = @AbilityHaste,
                AttackSpeed = @AttackSpeed,
                CriticalStrikeChance = @CriticalStrikeChance,
                MoveSpeed = @MoveSpeed,
                MoveSpeedPercent = @MoveSpeedPercent,
                Lethality = @Lethality,
                ArmorPenetrationPercent = @ArmorPenetrationPercent,
                MagicPenetration = @MagicPenetration,
                MagicPenetrationPercent = @MagicPenetrationPercent,
                LifeSteal = @LifeSteal,
                Omnivamp = @Omnivamp,
                HealAndShieldPower = @HealAndShieldPower,
                Tenacity = @Tenacity,
                Price = @Price,
                DataDragonId = @DataDragonId,
                ItemImageUrl = @ItemImageUrl,
                ItemTags = @ItemTags,
                ItemDescription = @ItemDescription
            WHERE Id = @Id AND Username = @User;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = equipment.Id;
        AddUsername(command, username);
        AddEquipmentParameters(command, equipment);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> DeleteAsync(
        string username,
        int id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(
            "DELETE FROM dbo.Equipments WHERE Id = @Id AND Username = @User;",
            connection);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        AddUsername(command, username);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<int> UpsertAsync(
        string username,
        Equipment equipment,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await EnsureEquipmentSchemaAsync(connection, cancellationToken);

        await using var command = new SqlCommand(UpsertEquipmentSql, connection);
        AddUsername(command, username);
        AddEquipmentParameters(command, equipment);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LoadoutSummary>> GetLoadoutsAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<(int Id, string Name, List<int> EquipmentIds)>();

        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT Id, LoadoutName, Eq1_Id, Eq2_Id, Eq3_Id, Eq4_Id, Eq5_Id, Eq6_Id
            FROM dbo.Loadouts
            WHERE Username = @User
            ORDER BY ISNULL(CreatedAt, '19000101') DESC, Id DESC;
            """;

        await using (var command = new SqlCommand(sql, connection))
        {
            AddUsername(command, username);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((
                    ReadInt32(reader, "Id"),
                    ReadString(reader, "LoadoutName", string.Empty),
                    ReadLoadoutEquipmentIds(reader)));
            }
        }

        var equipmentMap = await GetEquipmentNameMapAsync(
            connection,
            rows.SelectMany(row => row.EquipmentIds).Distinct().ToList(),
            username,
            cancellationToken);

        return rows.Select(row =>
        {
            var validItems = row.EquipmentIds
                .Where(equipmentMap.ContainsKey)
                .Select(id => new LoadoutItemSummary(id, equipmentMap[id]))
                .ToList();

            return new LoadoutSummary(
                row.Id,
                row.Name,
                validItems,
                validItems.Select(item => item.Id).ToList(),
                row.EquipmentIds.Count(id => !equipmentMap.ContainsKey(id)));
        }).ToList();
    }

    public async Task<IReadOnlyList<string>> SaveLoadoutAsync(
        string username,
        string loadoutName,
        IReadOnlyList<int> equipmentIds,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO dbo.Loadouts (Username, LoadoutName, Eq1_Id, Eq2_Id, Eq3_Id, Eq4_Id, Eq5_Id, Eq6_Id)
            VALUES (@User, @LoadoutName, @E1, @E2, @E3, @E4, @E5, @E6);
            """;

        await using (var command = new SqlCommand(sql, connection))
        {
            AddUsername(command, username);
            command.Parameters.Add("@LoadoutName", SqlDbType.NVarChar, 100).Value = loadoutName.Trim();
            AddLoadoutEquipmentParameters(command, equipmentIds);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return await GetEquipmentNamesAsync(connection, equipmentIds, username, cancellationToken);
    }

    public async Task<IReadOnlyList<string>?> UpdateLoadoutAsync(
        string username,
        int loadoutId,
        string loadoutName,
        IReadOnlyList<int> equipmentIds,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE dbo.Loadouts
            SET LoadoutName = @LoadoutName,
                Eq1_Id = @E1,
                Eq2_Id = @E2,
                Eq3_Id = @E3,
                Eq4_Id = @E4,
                Eq5_Id = @E5,
                Eq6_Id = @E6
            WHERE Id = @LoadoutId AND Username = @User;
            """;

        await using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.Add("@LoadoutId", SqlDbType.Int).Value = loadoutId;
            AddUsername(command, username);
            command.Parameters.Add("@LoadoutName", SqlDbType.NVarChar, 100).Value = loadoutName.Trim();
            AddLoadoutEquipmentParameters(command, equipmentIds);

            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                return null;
            }
        }

        return await GetEquipmentNamesAsync(connection, equipmentIds, username, cancellationToken);
    }

    public async Task<string?> DeleteLoadoutAsync(
        string username,
        int loadoutId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var deletedName = string.Empty;
        await using (var findCommand = new SqlCommand(
            "SELECT LoadoutName FROM dbo.Loadouts WHERE Id = @LoadoutId AND Username = @User;",
            connection))
        {
            findCommand.Parameters.Add("@LoadoutId", SqlDbType.Int).Value = loadoutId;
            AddUsername(findCommand, username);

            deletedName = await findCommand.ExecuteScalarAsync(cancellationToken) as string ?? string.Empty;
        }

        await using (var deleteCommand = new SqlCommand(
            "DELETE FROM dbo.Loadouts WHERE Id = @LoadoutId AND Username = @User;",
            connection))
        {
            deleteCommand.Parameters.Add("@LoadoutId", SqlDbType.Int).Value = loadoutId;
            AddUsername(deleteCommand, username);

            return await deleteCommand.ExecuteNonQueryAsync(cancellationToken) > 0
                ? deletedName
                : null;
        }
    }

    public async Task<int> DeleteInvalidLoadoutsAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            DELETE l
            FROM dbo.Loadouts l
            WHERE l.Username = @User
              AND NOT EXISTS (
                    SELECT 1
                    FROM (VALUES (l.Eq1_Id), (l.Eq2_Id), (l.Eq3_Id), (l.Eq4_Id), (l.Eq5_Id), (l.Eq6_Id)) v(EquipmentId)
                    INNER JOIN dbo.Equipments e ON e.Id = v.EquipmentId AND e.Username = l.Username
                    WHERE v.EquipmentId IS NOT NULL
              );
            """;

        await using var command = new SqlCommand(sql, connection);
        AddUsername(command, username);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string InsertEquipmentSql = """
        INSERT INTO dbo.Equipments
            (Username, Name, HP, Mana, Attack, MagicAttack, PhysicalDefense, MagicDefense,
             HealthRegen, ManaRegen, AbilityHaste, AttackSpeed, CriticalStrikeChance,
             MoveSpeed, MoveSpeedPercent, Lethality, ArmorPenetrationPercent,
             MagicPenetration, MagicPenetrationPercent, LifeSteal, Omnivamp,
             HealAndShieldPower, Tenacity, Price, DataDragonId, ItemImageUrl, ItemTags, ItemDescription)
        VALUES
            (@User, @Name, @HP, @Mana, @Attack, @MagicAttack, @PhysicalDefense, @MagicDefense,
             @HealthRegen, @ManaRegen, @AbilityHaste, @AttackSpeed, @CriticalStrikeChance,
             @MoveSpeed, @MoveSpeedPercent, @Lethality, @ArmorPenetrationPercent,
             @MagicPenetration, @MagicPenetrationPercent, @LifeSteal, @Omnivamp,
             @HealAndShieldPower, @Tenacity, @Price, @DataDragonId, @ItemImageUrl, @ItemTags, @ItemDescription);
        """;

    private const string UpsertEquipmentSql = """
        IF EXISTS (
            SELECT 1 FROM dbo.Equipments WHERE Username = @User AND Name = @Name
        )
        BEGIN
            UPDATE dbo.Equipments
            SET HP = @HP,
                Mana = @Mana,
                Attack = @Attack,
                MagicAttack = @MagicAttack,
                PhysicalDefense = @PhysicalDefense,
                MagicDefense = @MagicDefense,
                HealthRegen = @HealthRegen,
                ManaRegen = @ManaRegen,
                AbilityHaste = @AbilityHaste,
                AttackSpeed = @AttackSpeed,
                CriticalStrikeChance = @CriticalStrikeChance,
                MoveSpeed = @MoveSpeed,
                MoveSpeedPercent = @MoveSpeedPercent,
                Lethality = @Lethality,
                ArmorPenetrationPercent = @ArmorPenetrationPercent,
                MagicPenetration = @MagicPenetration,
                MagicPenetrationPercent = @MagicPenetrationPercent,
                LifeSteal = @LifeSteal,
                Omnivamp = @Omnivamp,
                HealAndShieldPower = @HealAndShieldPower,
                Tenacity = @Tenacity,
                Price = @Price,
                DataDragonId = @DataDragonId,
                ItemImageUrl = @ItemImageUrl,
                ItemTags = @ItemTags,
                ItemDescription = @ItemDescription
            WHERE Username = @User AND Name = @Name;
        END
        ELSE
        BEGIN
            INSERT INTO dbo.Equipments
                (Username, Name, HP, Mana, Attack, MagicAttack, PhysicalDefense, MagicDefense,
                 HealthRegen, ManaRegen, AbilityHaste, AttackSpeed, CriticalStrikeChance,
                 MoveSpeed, MoveSpeedPercent, Lethality, ArmorPenetrationPercent,
                 MagicPenetration, MagicPenetrationPercent, LifeSteal, Omnivamp,
                 HealAndShieldPower, Tenacity, Price, DataDragonId, ItemImageUrl, ItemTags, ItemDescription)
            VALUES
                (@User, @Name, @HP, @Mana, @Attack, @MagicAttack, @PhysicalDefense, @MagicDefense,
                 @HealthRegen, @ManaRegen, @AbilityHaste, @AttackSpeed, @CriticalStrikeChance,
                 @MoveSpeed, @MoveSpeedPercent, @Lethality, @ArmorPenetrationPercent,
                 @MagicPenetration, @MagicPenetrationPercent, @LifeSteal, @Omnivamp,
                 @HealAndShieldPower, @Tenacity, @Price, @DataDragonId, @ItemImageUrl, @ItemTags, @ItemDescription);
        END
        """;


    private static async Task EnsureEquipmentSchemaAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF COL_LENGTH(N'dbo.Equipments', N'Mana') IS NULL ALTER TABLE dbo.Equipments ADD Mana INT NOT NULL CONSTRAINT DF_Equipments_Mana DEFAULT (0);
            IF COL_LENGTH(N'dbo.Equipments', N'HealthRegen') IS NULL ALTER TABLE dbo.Equipments ADD HealthRegen DECIMAL(10,2) NOT NULL CONSTRAINT DF_Equipments_HealthRegen DEFAULT (0);
            IF COL_LENGTH(N'dbo.Equipments', N'ManaRegen') IS NULL ALTER TABLE dbo.Equipments ADD ManaRegen DECIMAL(10,2) NOT NULL CONSTRAINT DF_Equipments_ManaRegen DEFAULT (0);
            IF COL_LENGTH(N'dbo.Equipments', N'AbilityHaste') IS NULL ALTER TABLE dbo.Equipments ADD AbilityHaste DECIMAL(10,2) NOT NULL CONSTRAINT DF_Equipments_AbilityHaste DEFAULT (0);
            IF COL_LENGTH(N'dbo.Equipments', N'AttackSpeed') IS NULL ALTER TABLE dbo.Equipments ADD AttackSpeed DECIMAL(10,2) NOT NULL CONSTRAINT DF_Equipments_AttackSpeed DEFAULT (0);
            IF COL_LENGTH(N'dbo.Equipments', N'CriticalStrikeChance') IS NULL ALTER TABLE dbo.Equipments ADD CriticalStrikeChance DECIMAL(10,2) NOT NULL CONSTRAINT DF_Equipments_CriticalStrikeChance DEFAULT (0);
            IF COL_LENGTH(N'dbo.Equipments', N'MoveSpeed') IS NULL ALTER TABLE dbo.Equipments ADD MoveSpeed INT NOT NULL CONSTRAINT DF_Equipments_MoveSpeed DEFAULT (0);
            IF COL_LENGTH(N'dbo.Equipments', N'MoveSpeedPercent') IS NULL ALTER TABLE dbo.Equipments ADD MoveSpeedPercent DECIMAL(10,2) NOT NULL CONSTRAINT DF_Equipments_MoveSpeedPercent DEFAULT (0);
            IF COL_LENGTH(N'dbo.Equipments', N'Lethality') IS NULL ALTER TABLE dbo.Equipments ADD Lethality DECIMAL(10,2) NOT NULL CONSTRAINT DF_Equipments_Lethality DEFAULT (0);
            IF COL_LENGTH(N'dbo.Equipments', N'ArmorPenetrationPercent') IS NULL ALTER TABLE dbo.Equipments ADD ArmorPenetrationPercent DECIMAL(10,2) NOT NULL CONSTRAINT DF_Equipments_ArmorPenetrationPercent DEFAULT (0);
            IF COL_LENGTH(N'dbo.Equipments', N'MagicPenetration') IS NULL ALTER TABLE dbo.Equipments ADD MagicPenetration DECIMAL(10,2) NOT NULL CONSTRAINT DF_Equipments_MagicPenetration DEFAULT (0);
            IF COL_LENGTH(N'dbo.Equipments', N'MagicPenetrationPercent') IS NULL ALTER TABLE dbo.Equipments ADD MagicPenetrationPercent DECIMAL(10,2) NOT NULL CONSTRAINT DF_Equipments_MagicPenetrationPercent DEFAULT (0);
            IF COL_LENGTH(N'dbo.Equipments', N'LifeSteal') IS NULL ALTER TABLE dbo.Equipments ADD LifeSteal DECIMAL(10,2) NOT NULL CONSTRAINT DF_Equipments_LifeSteal DEFAULT (0);
            IF COL_LENGTH(N'dbo.Equipments', N'Omnivamp') IS NULL ALTER TABLE dbo.Equipments ADD Omnivamp DECIMAL(10,2) NOT NULL CONSTRAINT DF_Equipments_Omnivamp DEFAULT (0);
            IF COL_LENGTH(N'dbo.Equipments', N'HealAndShieldPower') IS NULL ALTER TABLE dbo.Equipments ADD HealAndShieldPower DECIMAL(10,2) NOT NULL CONSTRAINT DF_Equipments_HealAndShieldPower DEFAULT (0);
            IF COL_LENGTH(N'dbo.Equipments', N'Tenacity') IS NULL ALTER TABLE dbo.Equipments ADD Tenacity DECIMAL(10,2) NOT NULL CONSTRAINT DF_Equipments_Tenacity DEFAULT (0);
            IF COL_LENGTH(N'dbo.Equipments', N'DataDragonId') IS NULL ALTER TABLE dbo.Equipments ADD DataDragonId NVARCHAR(50) NULL;
            IF COL_LENGTH(N'dbo.Equipments', N'ItemImageUrl') IS NULL ALTER TABLE dbo.Equipments ADD ItemImageUrl NVARCHAR(500) NULL;
            IF COL_LENGTH(N'dbo.Equipments', N'ItemTags') IS NULL ALTER TABLE dbo.Equipments ADD ItemTags NVARCHAR(500) NULL;
            IF COL_LENGTH(N'dbo.Equipments', N'ItemDescription') IS NULL ALTER TABLE dbo.Equipments ADD ItemDescription NVARCHAR(2000) NULL;
            """;

        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string? BuildStatFilterCondition(string? statFilter)
    {
        return statFilter?.Trim().ToLowerInvariant() switch
        {
            "ad" => "Attack > 0",
            "ap" => "MagicAttack > 0",
            "health" => "HP > 0",
            "mana" => "Mana > 0",
            "armor" => "PhysicalDefense > 0",
            "magicresist" => "MagicDefense > 0",
            "defense" => "HP > 0 OR PhysicalDefense > 0 OR MagicDefense > 0 OR Tenacity > 0",
            "haste" => "AbilityHaste > 0",
            "attackspeed" => "AttackSpeed > 0",
            "crit" => "CriticalStrikeChance > 0",
            "movespeed" => "MoveSpeed > 0 OR MoveSpeedPercent > 0",
            "lethality" => "Lethality > 0",
            "armorpen" => "ArmorPenetrationPercent > 0",
            "magicpen" => "MagicPenetration > 0 OR MagicPenetrationPercent > 0",
            "sustain" => "HealthRegen > 0 OR ManaRegen > 0 OR LifeSteal > 0 OR Omnivamp > 0 OR HealAndShieldPower > 0",
            _ => null
        };
    }

    private static List<int> ReadLoadoutEquipmentIds(SqlDataReader reader)
    {
        var ids = new List<int>();
        for (var index = 1; index <= 6; index++)
        {
            var value = reader[$"Eq{index}_Id"];
            if (value != DBNull.Value && Convert.ToInt32(value) > 0)
            {
                ids.Add(Convert.ToInt32(value));
            }
        }

        return ids;
    }

    private static void AddLoadoutEquipmentParameters(SqlCommand command, IReadOnlyList<int> equipmentIds)
    {
        for (var index = 1; index <= 6; index++)
        {
            var parameter = command.Parameters.Add($"@E{index}", SqlDbType.Int);
            parameter.Value = index <= equipmentIds.Count
                ? equipmentIds[index - 1]
                : DBNull.Value;
        }
    }

    private static async Task<Dictionary<int, string>> GetEquipmentNameMapAsync(
        SqlConnection connection,
        IReadOnlyList<int> equipmentIds,
        string username,
        CancellationToken cancellationToken)
    {
        var idList = equipmentIds.Where(id => id > 0).Distinct().ToList();
        var result = new Dictionary<int, string>();
        if (idList.Count == 0)
        {
            return result;
        }

        var idParameters = idList.Select((_, index) => $"@Id{index}").ToArray();
        var sql = $"SELECT Id, Name FROM dbo.Equipments WHERE Username = @User AND Id IN ({string.Join(", ", idParameters)});";

        await using var command = new SqlCommand(sql, connection);
        AddUsername(command, username);
        for (var index = 0; index < idList.Count; index++)
        {
            command.Parameters.Add(idParameters[index], SqlDbType.Int).Value = idList[index];
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[ReadInt32(reader, "Id")] = ReadString(reader, "Name", string.Empty);
        }

        return result;
    }

    private static async Task<IReadOnlyList<string>> GetEquipmentNamesAsync(
        SqlConnection connection,
        IReadOnlyList<int> equipmentIds,
        string username,
        CancellationToken cancellationToken)
    {
        var equipmentMap = await GetEquipmentNameMapAsync(connection, equipmentIds, username, cancellationToken);

        return equipmentIds
            .Where(equipmentMap.ContainsKey)
            .Select(id => equipmentMap[id])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
    }

    private static void AddEquipmentParameters(SqlCommand command, Equipment equipment)
    {
        command.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = equipment.Name ?? string.Empty;
        command.Parameters.Add("@HP", SqlDbType.Int).Value = equipment.HP;
        command.Parameters.Add("@Mana", SqlDbType.Int).Value = equipment.Mana;
        command.Parameters.Add("@Attack", SqlDbType.Int).Value = equipment.Attack;
        command.Parameters.Add("@MagicAttack", SqlDbType.Int).Value = equipment.MagicAttack;
        command.Parameters.Add("@PhysicalDefense", SqlDbType.Int).Value = equipment.PhysicalDefense;
        command.Parameters.Add("@MagicDefense", SqlDbType.Int).Value = equipment.MagicDefense;
        AddDecimal(command, "@HealthRegen", equipment.HealthRegen);
        AddDecimal(command, "@ManaRegen", equipment.ManaRegen);
        AddDecimal(command, "@AbilityHaste", equipment.AbilityHaste);
        AddDecimal(command, "@AttackSpeed", equipment.AttackSpeed);
        AddDecimal(command, "@CriticalStrikeChance", equipment.CriticalStrikeChance);
        command.Parameters.Add("@MoveSpeed", SqlDbType.Int).Value = equipment.MoveSpeed;
        AddDecimal(command, "@MoveSpeedPercent", equipment.MoveSpeedPercent);
        AddDecimal(command, "@Lethality", equipment.Lethality);
        AddDecimal(command, "@ArmorPenetrationPercent", equipment.ArmorPenetrationPercent);
        AddDecimal(command, "@MagicPenetration", equipment.MagicPenetration);
        AddDecimal(command, "@MagicPenetrationPercent", equipment.MagicPenetrationPercent);
        AddDecimal(command, "@LifeSteal", equipment.LifeSteal);
        AddDecimal(command, "@Omnivamp", equipment.Omnivamp);
        AddDecimal(command, "@HealAndShieldPower", equipment.HealAndShieldPower);
        AddDecimal(command, "@Tenacity", equipment.Tenacity);
        command.Parameters.Add("@Price", SqlDbType.Int).Value = equipment.Price;
        AddNullableString(command, "@DataDragonId", equipment.DataDragonId, 50);
        AddNullableString(command, "@ItemImageUrl", equipment.ItemImageUrl, 500);
        AddNullableString(command, "@ItemTags", equipment.ItemTags, 500);
        AddNullableString(command, "@ItemDescription", equipment.ItemDescription, 2000);
    }

    private static void AddUsername(SqlCommand command, string username)
    {
        command.Parameters.Add("@User", SqlDbType.NVarChar, 100).Value = username;
    }

    private static void AddDecimal(SqlCommand command, string name, decimal value)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = 10;
        parameter.Scale = 2;
        parameter.Value = value;
    }

    private static void AddNullableString(SqlCommand command, string name, string? value, int size)
    {
        command.Parameters.Add(name, SqlDbType.NVarChar, size).Value =
            string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    private static Equipment ReadEquipment(SqlDataReader reader)
    {
        return new Equipment
        {
            Id = ReadInt32(reader, "Id"),
            Name = ReadString(reader, "Name", string.Empty),
            HP = ReadInt32(reader, "HP"),
            Mana = ReadInt32(reader, "Mana"),
            Attack = ReadInt32(reader, "Attack"),
            MagicAttack = ReadInt32(reader, "MagicAttack"),
            PhysicalDefense = ReadInt32(reader, "PhysicalDefense"),
            MagicDefense = ReadInt32(reader, "MagicDefense"),
            HealthRegen = ReadDecimal(reader, "HealthRegen"),
            ManaRegen = ReadDecimal(reader, "ManaRegen"),
            AbilityHaste = ReadDecimal(reader, "AbilityHaste"),
            AttackSpeed = ReadDecimal(reader, "AttackSpeed"),
            CriticalStrikeChance = ReadDecimal(reader, "CriticalStrikeChance"),
            MoveSpeed = ReadInt32(reader, "MoveSpeed"),
            MoveSpeedPercent = ReadDecimal(reader, "MoveSpeedPercent"),
            Lethality = ReadDecimal(reader, "Lethality"),
            ArmorPenetrationPercent = ReadDecimal(reader, "ArmorPenetrationPercent"),
            MagicPenetration = ReadDecimal(reader, "MagicPenetration"),
            MagicPenetrationPercent = ReadDecimal(reader, "MagicPenetrationPercent"),
            LifeSteal = ReadDecimal(reader, "LifeSteal"),
            Omnivamp = ReadDecimal(reader, "Omnivamp"),
            HealAndShieldPower = ReadDecimal(reader, "HealAndShieldPower"),
            Tenacity = ReadDecimal(reader, "Tenacity"),
            Price = ReadInt32(reader, "Price"),
            DataDragonId = ReadNullableString(reader, "DataDragonId"),
            ItemImageUrl = ReadNullableString(reader, "ItemImageUrl"),
            ItemTags = ReadNullableString(reader, "ItemTags"),
            ItemDescription = ReadNullableString(reader, "ItemDescription")
        };
    }

    private static string ReadString(SqlDataReader reader, string name, string fallback)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? fallback : reader.GetString(ordinal);
    }

    private static string? ReadNullableString(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
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

