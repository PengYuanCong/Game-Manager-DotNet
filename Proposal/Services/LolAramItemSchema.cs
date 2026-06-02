using Microsoft.Data.SqlClient;

namespace Proposal.Services
{
    internal static class LolAramItemSchema
    {
        public static async Task EnsureTableAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            const string createSql = @"
                IF OBJECT_ID(N'dbo.LolAramItems', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.LolAramItems
                    (
                        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        ItemKey NVARCHAR(100) NOT NULL,
                        Name NVARCHAR(100) NOT NULL,
                        Aliases NVARCHAR(500) NULL,
                        ModeName NVARCHAR(100) NOT NULL,
                        EffectText NVARCHAR(1200) NOT NULL,
                        Tags NVARCHAR(1000) NULL,
                        SynergyNotes NVARCHAR(1200) NULL,
                        PatchVersion NVARCHAR(50) NOT NULL,
                        SourceUrl NVARCHAR(1000) NULL,
                        Notes NVARCHAR(1200) NULL,
                        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_LolAramItems_CreatedAt DEFAULT SYSUTCDATETIME(),
                        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_LolAramItems_UpdatedAt DEFAULT SYSUTCDATETIME(),
                        CONSTRAINT UQ_LolAramItems_ItemKey_ModeName UNIQUE (ItemKey, ModeName)
                    )
                END";

            await using var command = new SqlCommand(createSql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public static async Task SeedStarterDataAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            const string seedSql = @"
                IF NOT EXISTS (SELECT 1 FROM LolAramItems WHERE ItemKey = N'blackfire_torch' AND ModeName = N'ARAM Mayhem')
                BEGIN
                    INSERT INTO LolAramItems
                        (ItemKey, Name, Aliases, ModeName, EffectText, Tags, SynergyNotes, PatchVersion, Notes)
                    VALUES
                        (N'blackfire_torch',
                         N'黑焰火炬',
                         N'黑焰; Blackfire Torch',
                         N'ARAM Mayhem',
                         N'提供持續傷害與燃燒相關的法師輸出價值。',
                         N'burn; damage_over_time; mage; spell; ap',
                         N'可提高需要燃燒觸發的海克斯價值，例如阿嬤的辣油。',
                         N'manual starter',
                         N'Starter item tag record; verify exact patch wording before treating as meta truth.')
                END

                IF NOT EXISTS (SELECT 1 FROM LolAramItems WHERE ItemKey = N'liandrys_torment' AND ModeName = N'ARAM Mayhem')
                BEGIN
                    INSERT INTO LolAramItems
                        (ItemKey, Name, Aliases, ModeName, EffectText, Tags, SynergyNotes, PatchVersion, Notes)
                    VALUES
                        (N'liandrys_torment',
                         N'蘭德里的折磨',
                         N'面具; 大面具; 蘭德里; Liandry; Liandry''s Torment',
                         N'ARAM Mayhem',
                         N'提供持續傷害與燃燒型輸出，適合技能能反覆命中的法師。',
                         N'burn; damage_over_time; mage; spell; ap; anti_tank',
                         N'可提高 burn tag 海克斯的推薦權重，尤其是布蘭德這類持續傷害英雄。',
                         N'manual starter',
                         N'User-highlighted burn item.')
                END

                IF NOT EXISTS (SELECT 1 FROM LolAramItems WHERE ItemKey = N'malignance' AND ModeName = N'ARAM Mayhem')
                BEGIN
                    INSERT INTO LolAramItems
                        (ItemKey, Name, Aliases, ModeName, EffectText, Tags, SynergyNotes, PatchVersion, Notes)
                    VALUES
                        (N'malignance',
                         N'惡意',
                         N'惡意; Malignance',
                         N'ARAM Mayhem',
                         N'偏向技能與大招後續傷害的法師裝備，玩家標記為燃燒/持續區域傷害來源。',
                         N'burn; damage_over_time; area; mage; spell; ap',
                         N'可與 burn、damage_over_time 海克斯一起評估，尤其是阿嬤的辣油這類吃燃燒觸發的效果。',
                         N'manual starter',
                         N'User-highlighted burn item.')
                END

                IF NOT EXISTS (SELECT 1 FROM LolAramItems WHERE ItemKey = N'sunfire_aegis' AND ModeName = N'ARAM Mayhem')
                BEGIN
                    INSERT INTO LolAramItems
                        (ItemKey, Name, Aliases, ModeName, EffectText, Tags, SynergyNotes, PatchVersion, Notes)
                    VALUES
                        (N'sunfire_aegis',
                         N'日炎聖盾',
                         N'火甲; 日炎; Sunfire; Sunfire Aegis',
                         N'ARAM Mayhem',
                         N'近身持續灼燒型坦克裝備，需要貼近敵人才能穩定發揮。',
                         N'burn; damage_over_time; area; tank; close_range',
                         N'能提供 burn tag，但對遠距法師價值取決於是否真的能站在敵人旁邊。',
                         N'manual starter',
                         N'User-highlighted burn item.')
                END";

            await using var command = new SqlCommand(seedSql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
