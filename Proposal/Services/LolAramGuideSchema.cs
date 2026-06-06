using Microsoft.Data.SqlClient;

namespace Proposal.Services
{
    internal static class LolAramGuideSchema
    {
        public static async Task EnsureTableAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            const string createSql = @"
                IF OBJECT_ID(N'dbo.LolAramGuides', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.LolAramGuides
                    (
                        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        ChampionKey NVARCHAR(100) NOT NULL,
                        ChampionName NVARCHAR(100) NOT NULL,
                        LocalizedName NVARCHAR(100) NULL,
                        ModeName NVARCHAR(100) NOT NULL,
                        PatchVersion NVARCHAR(50) NOT NULL,
                        RoleSummary NVARCHAR(500) NOT NULL,
                        CoreItems NVARCHAR(1000) NOT NULL,
                        SituationalItems NVARCHAR(1000) NULL,
                        Augments NVARCHAR(1000) NULL,
                        SummonerSpells NVARCHAR(500) NULL,
                        SkillOrder NVARCHAR(500) NULL,
                        PlaystyleTips NVARCHAR(1200) NULL,
                        PositioningTips NVARCHAR(1200) NULL,
                        Weaknesses NVARCHAR(1200) NULL,
                        SourceUrl NVARCHAR(1000) NULL,
                        Notes NVARCHAR(1200) NULL,
                        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_LolAramGuides_CreatedAt DEFAULT SYSUTCDATETIME(),
                        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_LolAramGuides_UpdatedAt DEFAULT SYSUTCDATETIME(),
                        CONSTRAINT UQ_LolAramGuides_ChampionKey_ModeName UNIQUE (ChampionKey, ModeName)
                    )
                END";

            await using var command = new SqlCommand(createSql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public static async Task SeedBrandGuideAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            const string seedSql = @"
                UPDATE LolAramGuides
                SET ChampionName = N'Brand',
                    LocalizedName = N'布蘭德',
                    PatchVersion = N'16.10 人工基準',
                    RoleSummary = N'AP 消耗與團戰燃燒法師，重視技能命中、持續傷害、魔法穿透與安全站位。',
                    CoreItems = N'黑焰火炬；蘭德里的折磨；瑞萊的冰晶節杖',
                    SituationalItems = N'法師之靴；虛空之杖；影焰；中婭沙漏；黑魔禁書；女妖面紗',
                    Augments = N'優先選燃燒、持續傷害、技能加速、法力續航、範圍傷害；敵方強開多時補防禦海克斯。',
                    SummonerSpells = N'閃現加雪球可進攻接招；被刺客壓力大時可選光盾或鬼步。',
                    SkillOrder = N'有大點大，主 W 打消耗與清線，之後依需求補 Q 控場或 E 擴散。',
                    PlaystyleTips = N'等敵人聚集後再交 R，利用狹窄地形觸發被動爆炸與燃燒擴散。',
                    PositioningTips = N'站在前排後方，避免第一時間被開；被刺客威脅時扣 Q 自保。',
                    Weaknesses = N'怕刺客貼身與硬開，若開戰前被逼出技能，團戰影響力會下降。',
                    SourceUrl = N'https://op.gg/zh-tw/lol/modes/aram-mayhem/brand/build',
                    Notes = N'人工整理的起始攻略，參考公開頁面與英雄機制，仍需依實際版本與對局修正。',
                    UpdatedAt = SYSUTCDATETIME()
                WHERE ChampionKey = N'brand'
                  AND ModeName = N'ARAM Mayhem'
                  AND (PatchVersion = N'16.09 reference'
                       OR CoreItems LIKE N'%Blackfire%'
                       OR RoleSummary LIKE N'%AP poke%')

                IF NOT EXISTS (
                    SELECT 1
                    FROM LolAramGuides
                    WHERE ChampionKey = N'brand' AND ModeName = N'ARAM Mayhem'
                )
                BEGIN
                    INSERT INTO LolAramGuides
                        (ChampionKey, ChampionName, LocalizedName, ModeName, PatchVersion, RoleSummary,
                         CoreItems, SituationalItems, Augments, SummonerSpells, SkillOrder,
                         PlaystyleTips, PositioningTips, Weaknesses, SourceUrl, Notes)
                    VALUES
                        (N'brand',
                         N'Brand',
                         N'布蘭德',
                         N'ARAM Mayhem',
                         N'16.10 人工基準',
                         N'AP 消耗與團戰燃燒法師，重視技能命中、持續傷害、魔法穿透與安全站位。',
                         N'黑焰火炬；蘭德里的折磨；瑞萊的冰晶節杖',
                         N'法師之靴；虛空之杖；影焰；中婭沙漏；黑魔禁書；女妖面紗',
                         N'優先選燃燒、持續傷害、技能加速、法力續航、範圍傷害；敵方強開多時補防禦海克斯。',
                         N'閃現加雪球可進攻接招；被刺客壓力大時可選光盾或鬼步。',
                         N'有大點大，主 W 打消耗與清線，之後依需求補 Q 控場或 E 擴散。',
                         N'等敵人聚集後再交 R，利用狹窄地形觸發被動爆炸與燃燒擴散。',
                         N'站在前排後方，避免第一時間被開；被刺客威脅時扣 Q 自保。',
                         N'怕刺客貼身與硬開，若開戰前被逼出技能，團戰影響力會下降。',
                         N'https://op.gg/zh-tw/lol/modes/aram-mayhem/brand/build',
                         N'人工整理的起始攻略，參考公開頁面與英雄機制，仍需依實際版本與對局修正。')
                END

                UPDATE LolAramGuides
                SET ChampionName = N'Seraphine',
                    LocalizedName = N'瑟菈紛',
                    PatchVersion = N'16.10 人工基準',
                    RoleSummary = N'AP 團隊型法師，擁有多段技能命中、範圍控制、護盾與加速，適合放大技能觸發、魔法傷害、護盾治療與團隊增益。',
                    CoreItems = N'黑焰火炬；蘭德里的折磨；熾天使的擁抱',
                    SituationalItems = N'月石再生器；流水法杖；瑞萊的冰晶節杖；虛空之杖；中婭沙漏；黑魔禁書',
                    Augments = N'稜彩優先：質變：大混亂、聖光顯靈、暴擊治療、魔法導彈、全部都給你、無限循環。黃金優先：阿嬤的辣油、風語者的祝福、女巫思維、靈光一閃、極度邪惡、虛空裂縫。白銀優先：靈魂淨化、雷射治療、老練狙擊手、射手法師、土司和起司、土司和奶油。',
                    SummonerSpells = N'閃現搭配光盾或鬼步；隊伍缺開戰時可帶雪球，但以保命與持續施法為主。',
                    SkillOrder = N'有大點大，主 Q 打消耗，副 E 提供控制；W 用來保護隊友與拉開距離。',
                    PlaystyleTips = N'用 Q/E 多段命中觸發海克斯，W 留給敵方開戰或隊友集結時，R 優先打穿越隊友與敵人的直線角度。',
                    PositioningTips = N'站在後排中線，讓技能同時覆蓋隊友與敵人；不要為了打滿 Q 走到刺客可開的位置。',
                    Weaknesses = N'怕突進刺客、沉默與硬控；若隊伍缺前排，需要更早補保命裝或防禦海克斯。',
                    SourceUrl = N'https://op.gg/zh-tw/lol/modes/aram-mayhem/seraphine/augments',
                    Notes = N'人工修正：以 AP、護盾、團隊增益、多段技能觸發為核心；純普攻或刺客向海克斯通常降級。',
                    UpdatedAt = SYSUTCDATETIME()
                WHERE ChampionKey = N'seraphine'
                  AND ModeName = N'ARAM Mayhem'
                  AND (Augments LIKE N'%評級邏輯%'
                       OR Augments LIKE N'%多段命中與技能觸發是 S/A%'
                       OR Augments LIKE N'%護盾治療與團隊增益是 A/B%')

                IF NOT EXISTS (
                    SELECT 1
                    FROM LolAramGuides
                    WHERE ChampionKey = N'seraphine' AND ModeName = N'ARAM Mayhem'
                )
                BEGIN
                    INSERT INTO LolAramGuides
                        (ChampionKey, ChampionName, LocalizedName, ModeName, PatchVersion, RoleSummary,
                         CoreItems, SituationalItems, Augments, SummonerSpells, SkillOrder,
                         PlaystyleTips, PositioningTips, Weaknesses, SourceUrl, Notes)
                    VALUES
                        (N'seraphine',
                         N'Seraphine',
                         N'瑟菈紛',
                         N'ARAM Mayhem',
                         N'16.10 人工基準',
                         N'AP 團隊型法師，擁有多段技能命中、範圍控制、護盾與加速，適合放大技能觸發、魔法傷害、護盾治療與團隊增益。',
                         N'黑焰火炬；蘭德里的折磨；熾天使的擁抱',
                         N'月石再生器；流水法杖；瑞萊的冰晶節杖；虛空之杖；中婭沙漏；黑魔禁書',
                         N'稜彩優先：質變：大混亂、聖光顯靈、暴擊治療、魔法導彈、全部都給你、無限循環。黃金優先：阿嬤的辣油、風語者的祝福、女巫思維、靈光一閃、極度邪惡、虛空裂縫。白銀優先：靈魂淨化、雷射治療、老練狙擊手、射手法師、土司和起司、土司和奶油。',
                         N'閃現搭配光盾或鬼步；隊伍缺開戰時可帶雪球，但以保命與持續施法為主。',
                         N'有大點大，主 Q 打消耗，副 E 提供控制；W 用來保護隊友與拉開距離。',
                         N'用 Q/E 多段命中觸發海克斯，W 留給敵方開戰或隊友集結時，R 優先打穿越隊友與敵人的直線角度。',
                         N'站在後排中線，讓技能同時覆蓋隊友與敵人；不要為了打滿 Q 走到刺客可開的位置。',
                         N'怕突進刺客、沉默與硬控；若隊伍缺前排，需要更早補保命裝或防禦海克斯。',
                         N'https://op.gg/zh-tw/lol/modes/aram-mayhem/seraphine/augments',
                         N'人工修正：以 AP、護盾、團隊增益、多段技能觸發為核心；純普攻或刺客向海克斯通常降級。')
                END";

            await using var command = new SqlCommand(seedSql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
