-- Minimal ARAM Mayhem seed data for a fresh Supabase staging database.
-- Full champion/augment/item data should be migrated from the existing SQL
-- Server database or refreshed through the approved import tools.

begin;

insert into public.lol_aram_augment_series
    (series_key, series_name, description, set_bonus_text, tags, patch_version, notes)
values
    ('snowball', 'Snowball', 'Snowball-oriented set', '(2) Improve snowball-related augments. (3) Further improve snowball pressure. (4) Max snowball set bonus.', 'snowball; engage; skillshot', 'manual starter', 'Starter set definition; replace with curated Traditional Chinese copy before production.'),
    ('self_destruct', 'Self Destruct', 'Bomb set that rewards revive timer pressure.', '(2) Reduce your revive countdown by 25%.', 'death; bomb; automation', 'manual starter', 'From project manual curation note.'),
    ('stacking_dino', 'Stackosaurus', 'Stacking set for augments that grow over time or on takedown.', '(2) Gain 50% additional stacks. (3) Gain 100% additional stacks. (4) Gain 200% additional stacks.', 'stackosaurus; takedown; scaling; extended_fight', 'manual starter', 'From project manual curation note.'),
    ('siren', 'Siren', 'Assistive set for low-health allies.', '(2) Gain 50% movement speed toward allied champions below 50% health. (3) Your next heal or shield restores 12% of the target lost health, 10 second cooldown per target.', 'healing; shield; ally; move_speed', 'manual starter', 'From project manual curation note.'),
    ('archmage', 'Archmage', 'Spell-casting set that refunds cooldown from another random ability.', '(2) Refund 30% cooldown to another random ability when you cast a spell.', 'spell; ability_haste; mage', 'manual starter', 'From project manual curation note.'),
    ('automation', 'Automation', 'Auto-cast set.', '(2) Reduce auto-cast cooldown by 30%. (3) Auto-cast cooldown benefits from ability haste.', 'automation; cooldown; proc', 'manual starter', 'From project manual curation note.'),
    ('high_roller', 'High Roller', 'Minion-drop and anvil economy set.', '(2) Minions may drop a stat bonus on death. (3) +20% chance to get gold or prismatic anvils. (4) +50% chance to get gold or prismatic anvils.', 'economy; anvil; minion; stat', 'manual starter', 'From project manual curation note.'),
    ('money_rain', 'Money Rain', 'Coin-drop set after takedowns.', '(2) Takedowns drop 6 coins. (3) Takedowns drop 12 coins. (4) Each coin is worth 5 gold and can only be picked up by the augment holder and allies.', 'economy; takedown; gold', 'manual starter', 'From project manual curation note.'),
    ('firecracker', 'Firecracker', 'Projectile bounce set.', '(2) Firecrackers bounce 2 times to nearby enemies for 25% original damage. (4) Firecrackers bounce 3 times for 50% original damage.', 'firecracker; missile; explosion; chain; area; proc', 'manual starter', 'From project manual curation note.')
on conflict (series_key) do update
set series_name = excluded.series_name,
    description = excluded.description,
    set_bonus_text = excluded.set_bonus_text,
    tags = excluded.tags,
    patch_version = excluded.patch_version,
    notes = excluded.notes;

insert into public.lol_aram_augments
    (augment_key, name, mode_name, rarity, tier, series_key, effect_text, tags, synergy_notes, patch_version, notes)
values
    ('grandma_spicy_oil', 'Grandma Spicy Oil', 'ARAM Mayhem', 'prismatic', 'S', null, 'Burn effects become stronger when paired with burn-capable champions or items.', 'burn; healing; area; damage_over_time', 'Pair with Blackfire Torch, Liandry Torment, Malignance, Sunfire Aegis, or champions with burn/damage-over-time kits.', 'manual starter', 'Translate to Traditional Chinese after final source verification.'),
    ('magic_missile', 'Magic Missile', 'ARAM Mayhem', 'gold', 'S', 'firecracker', 'Spell hits trigger missile pressure.', 'firecracker; missile; spell_hit; poke; proc; area', 'Excellent for multi-hit AP poke champions and Firecracker set stacking.', 'manual starter', 'Translate to Traditional Chinese after final source verification.'),
    ('extremely_evil', 'Extremely Evil', 'ARAM Mayhem', 'gold', 'S', 'stacking_dino', 'Stacking combat augment for longer fights.', 'stackosaurus; extended_fight; takedown; scaling', 'Best on champions that can survive and repeatedly participate in fights.', 'manual starter', 'Translate to Traditional Chinese after final source verification.')
on conflict (augment_key, mode_name) do update
set name = excluded.name,
    rarity = excluded.rarity,
    tier = excluded.tier,
    series_key = excluded.series_key,
    effect_text = excluded.effect_text,
    tags = excluded.tags,
    synergy_notes = excluded.synergy_notes,
    patch_version = excluded.patch_version,
    notes = excluded.notes;

insert into public.lol_aram_items
    (item_key, name, aliases, mode_name, effect_text, tags, synergy_notes, patch_version, notes)
values
    ('blackfire_torch', 'Blackfire Torch', 'blackfire; torch', 'ARAM Mayhem', 'AP burn item used by sustained spell damage champions.', 'burn; damage_over_time; mage; spell; ap', 'Triggers burn synergies and supports Grandma Spicy Oil style recommendations.', 'manual starter', 'Replace with imported item copy before production.'),
    ('liandrys_torment', 'Liandry Torment', 'liandry; liandrys', 'ARAM Mayhem', 'AP damage-over-time item for longer fights and higher-health targets.', 'burn; damage_over_time; mage; spell; ap; anti_tank', 'Pairs with burn synergies and sustained AP poke.', 'manual starter', 'Replace with imported item copy before production.'),
    ('malignance', 'Malignance', 'malignance', 'ARAM Mayhem', 'Ultimate-focused AP item with area pressure.', 'burn; damage_over_time; area; mage; spell; ap', 'Useful for AP champions that frequently affect fights with ultimate casts.', 'manual starter', 'Replace with imported item copy before production.'),
    ('sunfire_aegis', 'Sunfire Aegis', 'sunfire', 'ARAM Mayhem', 'Tank burn aura item for close-range fights.', 'burn; damage_over_time; area; tank; close_range', 'Burn tag can matter for augment synergy, but should not be forced on backline carries.', 'manual starter', 'Replace with imported item copy before production.')
on conflict (item_key, mode_name) do update
set name = excluded.name,
    aliases = excluded.aliases,
    effect_text = excluded.effect_text,
    tags = excluded.tags,
    synergy_notes = excluded.synergy_notes,
    patch_version = excluded.patch_version,
    notes = excluded.notes;

insert into public.lol_aram_synergy_rules
    (rule_name, boost_augment_key, series_key, trigger_tags, champion_tags, item_tags, condition_text, recommendation_text, priority, patch_version, notes)
values
    ('Burn package boosts Grandma Spicy Oil', 'grandma_spicy_oil', null, 'burn; damage_over_time', 'burn; mage; poke', 'burn; damage_over_time', 'Champion kit or current item plan already contains reliable burn or damage-over-time.', 'Raise Grandma Spicy Oil and burn-compatible items, but do not force AP burn items onto physical carries.', 'high', 'manual starter', 'Safety note added after AP/AD mismatch corrections.')
on conflict do nothing;

commit;
