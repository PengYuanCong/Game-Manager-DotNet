using Npgsql;
using NpgsqlTypes;
using Proposal.Models;

namespace Proposal.Services;

public sealed class PostgresCalculatorDataRepository : ICalculatorDataRepository
{
    private readonly IPostgresConnectionFactory _connectionFactory;

    public PostgresCalculatorDataRepository(IPostgresConnectionFactory connectionFactory)
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
            select id, name
            from public.equipments
            where owner_username = @username
            order by price asc, name asc;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddUsername(command, username);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            equipments.Add(new Equipment
            {
                Id = ReadInt32(reader, "id"),
                Name = ReadString(reader, "name", string.Empty)
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
            select l.id, l.loadout_name
            from public.equipment_loadouts l
            where l.owner_username = @username
              and exists (
                    select 1
                    from (values (l.eq1_id), (l.eq2_id), (l.eq3_id), (l.eq4_id), (l.eq5_id), (l.eq6_id)) v(equipment_id)
                    inner join public.equipments e on e.id = v.equipment_id and e.owner_username = l.owner_username
                    where v.equipment_id is not null
              )
            order by l.created_at desc, l.id desc;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddUsername(command, username);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            loadouts.Add(new Loadout
            {
                Id = ReadInt32(reader, "id"),
                LoadoutName = ReadString(reader, "loadout_name", string.Empty)
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
            select
                coalesce(sum(e.hp), 0) as total_hp,
                coalesce(sum(e.mana), 0) as total_mana,
                coalesce(sum(e.attack), 0) as total_attack,
                coalesce(sum(e.magic_attack), 0) as total_magic_attack,
                coalesce(sum(e.physical_defense), 0) as total_physical_defense,
                coalesce(sum(e.magic_defense), 0) as total_magic_defense,
                coalesce(sum(e.health_regen), 0) as total_health_regen,
                coalesce(sum(e.mana_regen), 0) as total_mana_regen,
                coalesce(sum(e.ability_haste), 0) as total_ability_haste,
                coalesce(sum(e.attack_speed), 0) as total_attack_speed,
                coalesce(sum(e.critical_strike_chance), 0) as total_crit,
                coalesce(sum(e.move_speed), 0) as total_move_speed,
                coalesce(sum(e.move_speed_percent), 0) as total_move_speed_percent,
                coalesce(sum(e.lethality), 0) as total_lethality,
                coalesce(sum(e.armor_penetration_percent), 0) as total_armor_penetration_percent,
                coalesce(sum(e.magic_penetration), 0) as total_magic_penetration,
                coalesce(sum(e.magic_penetration_percent), 0) as total_magic_penetration_percent,
                coalesce(sum(e.life_steal), 0) as total_life_steal,
                coalesce(sum(e.omnivamp), 0) as total_omnivamp,
                coalesce(sum(e.heal_and_shield_power), 0) as total_heal_and_shield_power,
                coalesce(sum(e.tenacity), 0) as total_tenacity,
                coalesce(sum(e.price), 0) as total_price
            from public.equipment_loadouts l
            left join lateral unnest(array[l.eq1_id, l.eq2_id, l.eq3_id, l.eq4_id, l.eq5_id, l.eq6_id]) as item_ids(item_id) on true
            left join public.equipments e on e.id = item_ids.item_id and e.owner_username = l.owner_username
            where l.id = @loadout_id and l.owner_username = @username;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("@loadout_id", NpgsqlDbType.Integer).Value = loadoutId;
        AddUsername(command, username);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new LoadoutStats
        {
            Hp = ReadInt32(reader, "total_hp"),
            Mana = ReadInt32(reader, "total_mana"),
            Attack = ReadInt32(reader, "total_attack"),
            MagicAttack = ReadInt32(reader, "total_magic_attack"),
            PDef = ReadInt32(reader, "total_physical_defense"),
            MDef = ReadInt32(reader, "total_magic_defense"),
            HealthRegen = ReadDecimal(reader, "total_health_regen"),
            ManaRegen = ReadDecimal(reader, "total_mana_regen"),
            AbilityHaste = ReadDecimal(reader, "total_ability_haste"),
            AttackSpeed = ReadDecimal(reader, "total_attack_speed"),
            CriticalStrikeChance = ReadDecimal(reader, "total_crit"),
            MoveSpeed = ReadInt32(reader, "total_move_speed"),
            MoveSpeedPercent = ReadDecimal(reader, "total_move_speed_percent"),
            Lethality = ReadDecimal(reader, "total_lethality"),
            ArmorPenetrationPercent = ReadDecimal(reader, "total_armor_penetration_percent"),
            MagicPenetration = ReadDecimal(reader, "total_magic_penetration"),
            MagicPenetrationPercent = ReadDecimal(reader, "total_magic_penetration_percent"),
            LifeSteal = ReadDecimal(reader, "total_life_steal"),
            Omnivamp = ReadDecimal(reader, "total_omnivamp"),
            HealAndShieldPower = ReadDecimal(reader, "total_heal_and_shield_power"),
            Tenacity = ReadDecimal(reader, "total_tenacity"),
            Price = ReadInt32(reader, "total_price")
        };
    }

    private static void AddUsername(NpgsqlCommand command, string username)
    {
        command.Parameters.Add("@username", NpgsqlDbType.Varchar, 100).Value = username.Trim();
    }

    private static string ReadString(NpgsqlDataReader reader, string name, string fallback)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? fallback : reader.GetString(ordinal);
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
