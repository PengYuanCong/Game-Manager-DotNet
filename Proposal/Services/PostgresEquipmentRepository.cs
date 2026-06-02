using System.Text;
using Npgsql;
using NpgsqlTypes;
using Proposal.Models;

namespace Proposal.Services;

public sealed class PostgresEquipmentRepository : IEquipmentRepository
{
    private const string EquipmentColumns = """
        id, name, hp, mana, attack, magic_attack, physical_defense, magic_defense,
        health_regen, mana_regen, ability_haste, attack_speed, critical_strike_chance,
        move_speed, move_speed_percent, lethality, armor_penetration_percent,
        magic_penetration, magic_penetration_percent, life_steal, omnivamp,
        heal_and_shield_power, tenacity, price, data_dragon_id, item_image_url,
        item_tags, item_description
        """;

    private readonly IPostgresConnectionFactory _connectionFactory;

    public PostgresEquipmentRepository(IPostgresConnectionFactory connectionFactory)
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

        var sql = new StringBuilder($"""
            select {EquipmentColumns}
            from public.equipments
            where owner_username = @username
            """);

        if (!string.IsNullOrWhiteSpace(searchString))
        {
            sql.AppendLine("and name ilike @search");
        }

        var statCondition = BuildStatFilterCondition(statFilter);
        if (!string.IsNullOrWhiteSpace(statCondition))
        {
            sql.AppendLine($"and ({statCondition})");
        }

        sql.AppendLine("order by price asc, name asc;");

        await using var command = new NpgsqlCommand(sql.ToString(), connection);
        AddUsername(command, username);
        if (!string.IsNullOrWhiteSpace(searchString))
        {
            command.Parameters.Add("@search", NpgsqlDbType.Varchar, 200).Value = $"%{searchString.Trim()}%";
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

        var sql = $"select {EquipmentColumns} from public.equipments where id = @id and owner_username = @username;";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("@id", NpgsqlDbType.Integer).Value = id;
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

        await using var command = new NpgsqlCommand(InsertEquipmentSql, connection);
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

        foreach (var equipment in rows)
        {
            await using var command = new NpgsqlCommand(InsertEquipmentSql, connection);
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

        const string sql = """
            update public.equipments
            set name = @name,
                hp = @hp,
                mana = @mana,
                attack = @attack,
                magic_attack = @magic_attack,
                physical_defense = @physical_defense,
                magic_defense = @magic_defense,
                health_regen = @health_regen,
                mana_regen = @mana_regen,
                ability_haste = @ability_haste,
                attack_speed = @attack_speed,
                critical_strike_chance = @critical_strike_chance,
                move_speed = @move_speed,
                move_speed_percent = @move_speed_percent,
                lethality = @lethality,
                armor_penetration_percent = @armor_penetration_percent,
                magic_penetration = @magic_penetration,
                magic_penetration_percent = @magic_penetration_percent,
                life_steal = @life_steal,
                omnivamp = @omnivamp,
                heal_and_shield_power = @heal_and_shield_power,
                tenacity = @tenacity,
                price = @price,
                data_dragon_id = @data_dragon_id,
                item_image_url = @item_image_url,
                item_tags = @item_tags,
                item_description = @item_description
            where id = @id and owner_username = @username;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("@id", NpgsqlDbType.Integer).Value = equipment.Id;
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

        await using var command = new NpgsqlCommand(
            "delete from public.equipments where id = @id and owner_username = @username;",
            connection);
        command.Parameters.Add("@id", NpgsqlDbType.Integer).Value = id;
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

        await using var command = new NpgsqlCommand(UpsertEquipmentSql, connection);
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
            select id, loadout_name, eq1_id, eq2_id, eq3_id, eq4_id, eq5_id, eq6_id
            from public.equipment_loadouts
            where owner_username = @username
            order by created_at desc, id desc;
            """;

        await using (var command = new NpgsqlCommand(sql, connection))
        {
            AddUsername(command, username);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((
                    ReadInt32(reader, "id"),
                    ReadString(reader, "loadout_name", string.Empty),
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
            insert into public.equipment_loadouts
                (owner_username, loadout_name, eq1_id, eq2_id, eq3_id, eq4_id, eq5_id, eq6_id)
            values
                (@username, @loadout_name, @e1, @e2, @e3, @e4, @e5, @e6);
            """;

        await using (var command = new NpgsqlCommand(sql, connection))
        {
            AddUsername(command, username);
            command.Parameters.Add("@loadout_name", NpgsqlDbType.Varchar, 100).Value = loadoutName.Trim();
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
            update public.equipment_loadouts
            set loadout_name = @loadout_name,
                eq1_id = @e1,
                eq2_id = @e2,
                eq3_id = @e3,
                eq4_id = @e4,
                eq5_id = @e5,
                eq6_id = @e6
            where id = @loadout_id and owner_username = @username;
            """;

        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.Add("@loadout_id", NpgsqlDbType.Integer).Value = loadoutId;
            AddUsername(command, username);
            command.Parameters.Add("@loadout_name", NpgsqlDbType.Varchar, 100).Value = loadoutName.Trim();
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
        await using (var findCommand = new NpgsqlCommand(
            "select loadout_name from public.equipment_loadouts where id = @loadout_id and owner_username = @username;",
            connection))
        {
            findCommand.Parameters.Add("@loadout_id", NpgsqlDbType.Integer).Value = loadoutId;
            AddUsername(findCommand, username);

            deletedName = await findCommand.ExecuteScalarAsync(cancellationToken) as string ?? string.Empty;
        }

        await using (var deleteCommand = new NpgsqlCommand(
            "delete from public.equipment_loadouts where id = @loadout_id and owner_username = @username;",
            connection))
        {
            deleteCommand.Parameters.Add("@loadout_id", NpgsqlDbType.Integer).Value = loadoutId;
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
            delete from public.equipment_loadouts l
            where l.owner_username = @username
              and not exists (
                    select 1
                    from (values (l.eq1_id), (l.eq2_id), (l.eq3_id), (l.eq4_id), (l.eq5_id), (l.eq6_id)) v(equipment_id)
                    inner join public.equipments e on e.id = v.equipment_id and e.owner_username = l.owner_username
                    where v.equipment_id is not null
              );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddUsername(command, username);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string InsertEquipmentSql = """
        insert into public.equipments
            (owner_username, name, hp, mana, attack, magic_attack, physical_defense, magic_defense,
             health_regen, mana_regen, ability_haste, attack_speed, critical_strike_chance,
             move_speed, move_speed_percent, lethality, armor_penetration_percent,
             magic_penetration, magic_penetration_percent, life_steal, omnivamp,
             heal_and_shield_power, tenacity, price, data_dragon_id, item_image_url, item_tags, item_description)
        values
            (@username, @name, @hp, @mana, @attack, @magic_attack, @physical_defense, @magic_defense,
             @health_regen, @mana_regen, @ability_haste, @attack_speed, @critical_strike_chance,
             @move_speed, @move_speed_percent, @lethality, @armor_penetration_percent,
             @magic_penetration, @magic_penetration_percent, @life_steal, @omnivamp,
             @heal_and_shield_power, @tenacity, @price, @data_dragon_id, @item_image_url, @item_tags, @item_description);
        """;

    private const string UpsertEquipmentSql = """
        insert into public.equipments
            (owner_username, name, hp, mana, attack, magic_attack, physical_defense, magic_defense,
             health_regen, mana_regen, ability_haste, attack_speed, critical_strike_chance,
             move_speed, move_speed_percent, lethality, armor_penetration_percent,
             magic_penetration, magic_penetration_percent, life_steal, omnivamp,
             heal_and_shield_power, tenacity, price, data_dragon_id, item_image_url, item_tags, item_description)
        values
            (@username, @name, @hp, @mana, @attack, @magic_attack, @physical_defense, @magic_defense,
             @health_regen, @mana_regen, @ability_haste, @attack_speed, @critical_strike_chance,
             @move_speed, @move_speed_percent, @lethality, @armor_penetration_percent,
             @magic_penetration, @magic_penetration_percent, @life_steal, @omnivamp,
             @heal_and_shield_power, @tenacity, @price, @data_dragon_id, @item_image_url, @item_tags, @item_description)
        on conflict (owner_username, (lower(name))) do update
        set hp = excluded.hp,
            mana = excluded.mana,
            attack = excluded.attack,
            magic_attack = excluded.magic_attack,
            physical_defense = excluded.physical_defense,
            magic_defense = excluded.magic_defense,
            health_regen = excluded.health_regen,
            mana_regen = excluded.mana_regen,
            ability_haste = excluded.ability_haste,
            attack_speed = excluded.attack_speed,
            critical_strike_chance = excluded.critical_strike_chance,
            move_speed = excluded.move_speed,
            move_speed_percent = excluded.move_speed_percent,
            lethality = excluded.lethality,
            armor_penetration_percent = excluded.armor_penetration_percent,
            magic_penetration = excluded.magic_penetration,
            magic_penetration_percent = excluded.magic_penetration_percent,
            life_steal = excluded.life_steal,
            omnivamp = excluded.omnivamp,
            heal_and_shield_power = excluded.heal_and_shield_power,
            tenacity = excluded.tenacity,
            price = excluded.price,
            data_dragon_id = excluded.data_dragon_id,
            item_image_url = excluded.item_image_url,
            item_tags = excluded.item_tags,
            item_description = excluded.item_description;
        """;

    private static string? BuildStatFilterCondition(string? statFilter)
    {
        return statFilter?.Trim().ToLowerInvariant() switch
        {
            "ad" => "attack > 0",
            "ap" => "magic_attack > 0",
            "health" => "hp > 0",
            "mana" => "mana > 0",
            "armor" => "physical_defense > 0",
            "magicresist" => "magic_defense > 0",
            "defense" => "hp > 0 or physical_defense > 0 or magic_defense > 0 or tenacity > 0",
            "haste" => "ability_haste > 0",
            "attackspeed" => "attack_speed > 0",
            "crit" => "critical_strike_chance > 0",
            "movespeed" => "move_speed > 0 or move_speed_percent > 0",
            "lethality" => "lethality > 0",
            "armorpen" => "armor_penetration_percent > 0",
            "magicpen" => "magic_penetration > 0 or magic_penetration_percent > 0",
            "sustain" => "health_regen > 0 or mana_regen > 0 or life_steal > 0 or omnivamp > 0 or heal_and_shield_power > 0",
            _ => null
        };
    }

    private static List<int> ReadLoadoutEquipmentIds(NpgsqlDataReader reader)
    {
        var ids = new List<int>();
        for (var index = 1; index <= 6; index++)
        {
            var value = reader[$"eq{index}_id"];
            if (value != DBNull.Value && Convert.ToInt32(value) > 0)
            {
                ids.Add(Convert.ToInt32(value));
            }
        }

        return ids;
    }

    private static void AddLoadoutEquipmentParameters(NpgsqlCommand command, IReadOnlyList<int> equipmentIds)
    {
        for (var index = 1; index <= 6; index++)
        {
            var parameter = command.Parameters.Add($"@e{index}", NpgsqlDbType.Bigint);
            parameter.Value = index <= equipmentIds.Count
                ? equipmentIds[index - 1]
                : DBNull.Value;
        }
    }

    private static async Task<Dictionary<int, string>> GetEquipmentNameMapAsync(
        NpgsqlConnection connection,
        IReadOnlyList<int> equipmentIds,
        string username,
        CancellationToken cancellationToken)
    {
        var idList = equipmentIds.Where(id => id > 0).Distinct().ToArray();
        var result = new Dictionary<int, string>();
        if (idList.Length == 0)
        {
            return result;
        }

        const string sql = """
            select id, name
            from public.equipments
            where owner_username = @username and id = any(@ids);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddUsername(command, username);
        command.Parameters.Add("@ids", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = idList;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[ReadInt32(reader, "id")] = ReadString(reader, "name", string.Empty);
        }

        return result;
    }

    private static async Task<IReadOnlyList<string>> GetEquipmentNamesAsync(
        NpgsqlConnection connection,
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

    private static void AddEquipmentParameters(NpgsqlCommand command, Equipment equipment)
    {
        command.Parameters.Add("@name", NpgsqlDbType.Varchar, 100).Value = equipment.Name ?? string.Empty;
        command.Parameters.Add("@hp", NpgsqlDbType.Integer).Value = equipment.HP;
        command.Parameters.Add("@mana", NpgsqlDbType.Integer).Value = equipment.Mana;
        command.Parameters.Add("@attack", NpgsqlDbType.Integer).Value = equipment.Attack;
        command.Parameters.Add("@magic_attack", NpgsqlDbType.Integer).Value = equipment.MagicAttack;
        command.Parameters.Add("@physical_defense", NpgsqlDbType.Integer).Value = equipment.PhysicalDefense;
        command.Parameters.Add("@magic_defense", NpgsqlDbType.Integer).Value = equipment.MagicDefense;
        AddDecimal(command, "@health_regen", equipment.HealthRegen);
        AddDecimal(command, "@mana_regen", equipment.ManaRegen);
        AddDecimal(command, "@ability_haste", equipment.AbilityHaste);
        AddDecimal(command, "@attack_speed", equipment.AttackSpeed);
        AddDecimal(command, "@critical_strike_chance", equipment.CriticalStrikeChance);
        command.Parameters.Add("@move_speed", NpgsqlDbType.Integer).Value = equipment.MoveSpeed;
        AddDecimal(command, "@move_speed_percent", equipment.MoveSpeedPercent);
        AddDecimal(command, "@lethality", equipment.Lethality);
        AddDecimal(command, "@armor_penetration_percent", equipment.ArmorPenetrationPercent);
        AddDecimal(command, "@magic_penetration", equipment.MagicPenetration);
        AddDecimal(command, "@magic_penetration_percent", equipment.MagicPenetrationPercent);
        AddDecimal(command, "@life_steal", equipment.LifeSteal);
        AddDecimal(command, "@omnivamp", equipment.Omnivamp);
        AddDecimal(command, "@heal_and_shield_power", equipment.HealAndShieldPower);
        AddDecimal(command, "@tenacity", equipment.Tenacity);
        command.Parameters.Add("@price", NpgsqlDbType.Integer).Value = equipment.Price;
        AddNullableString(command, "@data_dragon_id", equipment.DataDragonId, 50);
        AddNullableString(command, "@item_image_url", equipment.ItemImageUrl, 500);
        AddNullableString(command, "@item_tags", equipment.ItemTags, 500);
        AddNullableString(command, "@item_description", equipment.ItemDescription, 2000);
    }

    private static void AddUsername(NpgsqlCommand command, string username)
    {
        command.Parameters.Add("@username", NpgsqlDbType.Varchar, 100).Value = username.Trim();
    }

    private static void AddDecimal(NpgsqlCommand command, string name, decimal value)
    {
        command.Parameters.Add(name, NpgsqlDbType.Numeric).Value = value;
    }

    private static void AddNullableString(NpgsqlCommand command, string name, string? value, int size)
    {
        command.Parameters.Add(name, NpgsqlDbType.Varchar, size).Value =
            string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    private static Equipment ReadEquipment(NpgsqlDataReader reader)
    {
        return new Equipment
        {
            Id = ReadInt32(reader, "id"),
            Name = ReadString(reader, "name", string.Empty),
            HP = ReadInt32(reader, "hp"),
            Mana = ReadInt32(reader, "mana"),
            Attack = ReadInt32(reader, "attack"),
            MagicAttack = ReadInt32(reader, "magic_attack"),
            PhysicalDefense = ReadInt32(reader, "physical_defense"),
            MagicDefense = ReadInt32(reader, "magic_defense"),
            HealthRegen = ReadDecimal(reader, "health_regen"),
            ManaRegen = ReadDecimal(reader, "mana_regen"),
            AbilityHaste = ReadDecimal(reader, "ability_haste"),
            AttackSpeed = ReadDecimal(reader, "attack_speed"),
            CriticalStrikeChance = ReadDecimal(reader, "critical_strike_chance"),
            MoveSpeed = ReadInt32(reader, "move_speed"),
            MoveSpeedPercent = ReadDecimal(reader, "move_speed_percent"),
            Lethality = ReadDecimal(reader, "lethality"),
            ArmorPenetrationPercent = ReadDecimal(reader, "armor_penetration_percent"),
            MagicPenetration = ReadDecimal(reader, "magic_penetration"),
            MagicPenetrationPercent = ReadDecimal(reader, "magic_penetration_percent"),
            LifeSteal = ReadDecimal(reader, "life_steal"),
            Omnivamp = ReadDecimal(reader, "omnivamp"),
            HealAndShieldPower = ReadDecimal(reader, "heal_and_shield_power"),
            Tenacity = ReadDecimal(reader, "tenacity"),
            Price = ReadInt32(reader, "price"),
            DataDragonId = ReadNullableString(reader, "data_dragon_id"),
            ItemImageUrl = ReadNullableString(reader, "item_image_url"),
            ItemTags = ReadNullableString(reader, "item_tags"),
            ItemDescription = ReadNullableString(reader, "item_description")
        };
    }

    private static string ReadString(NpgsqlDataReader reader, string name, string fallback)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? fallback : reader.GetString(ordinal);
    }

    private static string? ReadNullableString(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int ReadInt32(NpgsqlDataReader reader, string name)
    {
        var value = reader[name];
        return value == DBNull.Value ? 0 : Convert.ToInt32(value);
    }

    private static decimal ReadDecimal(NpgsqlDataReader reader, string name)
    {
        var value = reader[name];
        return value == DBNull.Value ? 0 : Convert.ToDecimal(value);
    }
}
