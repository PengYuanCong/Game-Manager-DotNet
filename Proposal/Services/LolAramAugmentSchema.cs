using Microsoft.Data.SqlClient;

namespace Proposal.Services
{
    internal static class LolAramAugmentSchema
    {
        public static async Task EnsureTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            const string createSql = @"
                IF OBJECT_ID(N'dbo.LolAramAugmentSeries', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.LolAramAugmentSeries
                    (
                        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        SeriesKey NVARCHAR(100) NOT NULL,
                        SeriesName NVARCHAR(100) NOT NULL,
                        Description NVARCHAR(1000) NULL,
                        SetBonusText NVARCHAR(1200) NULL,
                        Tags NVARCHAR(1000) NULL,
                        PatchVersion NVARCHAR(50) NOT NULL,
                        SourceUrl NVARCHAR(1000) NULL,
                        Notes NVARCHAR(1200) NULL,
                        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_LolAramAugmentSeries_CreatedAt DEFAULT SYSUTCDATETIME(),
                        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_LolAramAugmentSeries_UpdatedAt DEFAULT SYSUTCDATETIME(),
                        CONSTRAINT UQ_LolAramAugmentSeries_SeriesKey UNIQUE (SeriesKey)
                    )
                END

                IF OBJECT_ID(N'dbo.LolAramAugments', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.LolAramAugments
                    (
                        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        AugmentKey NVARCHAR(100) NOT NULL,
                        Name NVARCHAR(100) NOT NULL,
                        ModeName NVARCHAR(100) NOT NULL,
                        Rarity NVARCHAR(50) NOT NULL,
                        Tier NVARCHAR(20) NULL,
                        SeriesKey NVARCHAR(100) NULL,
                        EffectText NVARCHAR(1200) NOT NULL,
                        Tags NVARCHAR(1000) NULL,
                        SynergyNotes NVARCHAR(1200) NULL,
                        PatchVersion NVARCHAR(50) NOT NULL,
                        SourceUrl NVARCHAR(1000) NULL,
                        Notes NVARCHAR(1200) NULL,
                        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_LolAramAugments_CreatedAt DEFAULT SYSUTCDATETIME(),
                        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_LolAramAugments_UpdatedAt DEFAULT SYSUTCDATETIME(),
                        CONSTRAINT UQ_LolAramAugments_AugmentKey_ModeName UNIQUE (AugmentKey, ModeName)
                    )
                END

                IF OBJECT_ID(N'dbo.LolAramSynergyRules', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.LolAramSynergyRules
                    (
                        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        RuleName NVARCHAR(150) NOT NULL,
                        BoostAugmentKey NVARCHAR(100) NULL,
                        SeriesKey NVARCHAR(100) NULL,
                        TriggerTags NVARCHAR(1000) NULL,
                        ChampionTags NVARCHAR(1000) NULL,
                        ItemTags NVARCHAR(1000) NULL,
                        ConditionText NVARCHAR(1200) NOT NULL,
                        RecommendationText NVARCHAR(1200) NOT NULL,
                        Priority NVARCHAR(20) NOT NULL,
                        PatchVersion NVARCHAR(50) NOT NULL,
                        Notes NVARCHAR(1200) NULL,
                        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_LolAramSynergyRules_CreatedAt DEFAULT SYSUTCDATETIME(),
                        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_LolAramSynergyRules_UpdatedAt DEFAULT SYSUTCDATETIME()
                    )
                END";

            await using var command = new SqlCommand(createSql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public static async Task SeedStarterDataAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            const string seedSql = @"
                IF NOT EXISTS (SELECT 1 FROM LolAramAugmentSeries WHERE SeriesKey = N'firecracker')
                BEGIN
                    INSERT INTO LolAramAugmentSeries
                        (SeriesKey, SeriesName, Description, SetBonusText, Tags, PatchVersion, Notes)
                    VALUES
                        (N'firecracker',
                         N'爆竹系列',
                         N'以飛彈、爆炸、額外觸發或連鎖傷害為核心的系列。',
                         N'多個爆竹系列海克斯同時出現時，優先檢查是否能提高飛彈觸發頻率、範圍傷害或收割能力。',
                         N'missile; explosion; chain; area; proc',
                         N'manual starter',
                         N'用來描述魔法導彈這類同系列搭配。')
                END

                IF NOT EXISTS (SELECT 1 FROM LolAramAugmentSeries WHERE SeriesKey = N'stacking_dino')
                BEGIN
                    INSERT INTO LolAramAugmentSeries
                        (SeriesKey, SeriesName, Description, SetBonusText, Tags, PatchVersion, Notes)
                    VALUES
                        (N'stacking_dino',
                         N'堆疊暴龍系列',
                         N'以戰鬥時間、擊殺助攻或重複觸發來累積層數的成長型系列。',
                         N'拿到同系列時，要評估英雄能不能穩定參戰與存活，越能持續作戰越有價值。',
                         N'stackosaurus; takedown; extended_fight',
                         N'manual starter',
                         N'用來描述極度邪惡這類堆疊暴龍搭配。')
                END

                IF NOT EXISTS (SELECT 1 FROM LolAramAugments WHERE AugmentKey = N'grandma_spicy_oil' AND ModeName = N'ARAM Mayhem')
                BEGIN
                    INSERT INTO LolAramAugments
                        (AugmentKey, Name, ModeName, Rarity, Tier, SeriesKey, EffectText, Tags, SynergyNotes, PatchVersion, Notes)
                    VALUES
                        (N'grandma_spicy_oil',
                         N'阿嬤的辣油',
                         N'ARAM Mayhem',
                         N'prismatic',
                         N'S',
                         NULL,
                         N'施加燃燒效果時，會在附近產生辣油區域；辣油可提供回復並讓敵人燃燒，燃燒觸發越多效果越強。',
                         N'burn; healing; area; damage_over_time',
                         N'本身沒有系列，但只要英雄、裝備或其他海克斯能提高燃燒頻率，它的價值就會上升。',
                         N'manual starter',
                         N'布蘭德、黑焰火炬、蘭德里的折磨這類燃燒來源可提高它的觸發價值。')
                END

                IF NOT EXISTS (SELECT 1 FROM LolAramAugments WHERE AugmentKey = N'magic_missile' AND ModeName = N'ARAM Mayhem')
                BEGIN
                    INSERT INTO LolAramAugments
                        (AugmentKey, Name, ModeName, Rarity, Tier, SeriesKey, EffectText, Tags, SynergyNotes, PatchVersion, Notes)
                    VALUES
                        (N'magic_missile',
                         N'魔法導彈',
                         N'ARAM Mayhem',
                         N'gold',
                         N'S',
                         N'firecracker',
                         N'偏向技能命中或施法後追加飛彈傷害的海克斯，適合高頻施法與遠距消耗英雄。',
                         N'missile; spell_hit; poke; proc; area',
                         N'如果同時有爆竹系列或能提高技能命中頻率的選項，推薦度提高。',
                         N'manual starter',
                         N'先作為系列與標籤示範資料，之後可用實測文字修正。')
                END

                IF NOT EXISTS (SELECT 1 FROM LolAramAugments WHERE AugmentKey = N'extremely_evil' AND ModeName = N'ARAM Mayhem')
                BEGIN
                    INSERT INTO LolAramAugments
                        (AugmentKey, Name, ModeName, Rarity, Tier, SeriesKey, EffectText, Tags, SynergyNotes, PatchVersion, Notes)
                    VALUES
                        (N'extremely_evil',
                         N'極度邪惡',
                         N'ARAM Mayhem',
                         N'gold',
                         N'S',
                         N'stacking_dino',
                         N'偏向累積層數與長時間成長的海克斯，適合能持續參戰、穩定拿助攻或反覆觸發效果的英雄。',
                         N'stackosaurus; extended_fight; takedown',
                         N'如果隊伍能打長團或英雄生存能力高，疊層暴龍系列價值提高。',
                         N'manual starter',
                         N'先作為系列與標籤示範資料，之後可用實測文字修正。')
                END

                IF NOT EXISTS (SELECT 1 FROM LolAramSynergyRules WHERE RuleName = N'燃燒提高阿嬤的辣油價值')
                BEGIN
                    INSERT INTO LolAramSynergyRules
                        (RuleName, BoostAugmentKey, SeriesKey, TriggerTags, ChampionTags, ItemTags,
                         ConditionText, RecommendationText, Priority, PatchVersion, Notes)
                    VALUES
                        (N'燃燒提高阿嬤的辣油價值',
                         N'grandma_spicy_oil',
                         NULL,
                         N'burn; damage_over_time',
                         N'burn; mage; poke',
                         N'burn; damage_over_time',
                         N'英雄、裝備或其他海克斯能穩定施加燃燒時，阿嬤的辣油會更容易反覆觸發。',
                         N'若玩家使用布蘭德或已有黑焰火炬、蘭德里的折磨這類燃燒來源，應提高阿嬤的辣油推薦權重。',
                         N'high',
                         N'manual starter',
                         N'這是非系列但由效果文字產生的 tag synergy。')
                END";

            await using var command = new SqlCommand(seedSql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
