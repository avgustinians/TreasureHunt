using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Facepunch;

namespace Oxide.Plugins
{
    [Info("Treasure Hunt", "Avgust", "2.9.25")]
    [Description("Treasure maps of four rarities that lead players to buried loot chests")]
    public class TreasureHunt : RustPlugin
    {

        // FileStorage хранит картинку по (entityId, crc), поэтому кладём её под собственным crc
        private static uint FsStorePng(byte[] data)
        {
            var id = CommunityEntity.ServerInstance.net.ID;
            ulong tmp = FsCrc32(data);
            uint crc = FileStorage.server.Store(id, tmp, FileStorage.Type.png, data);
            if (crc != tmp)
            {
                FileStorage.server.Store(id, crc, FileStorage.Type.png, data);
                FileStorage.server.Remove(id, tmp);
            }
            return crc;
        }

        private static uint FsCrc32(byte[] data)
        {
            uint c = 0xFFFFFFFFu;
            for (int i = 0; i < data.Length; i++)
            {
                c ^= data[i];
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320u : c >> 1;
            }
            return ~c;
        }

        #region Configuration

        private const string DefaultAdminPermission = "treasurehunt.admin";

        [PluginReference]
        private Plugin CustomItemDefinitionsPlugin;

        private ConfigData config;
        private bool customItemDefinitionsRegistered;
        private bool customItemDefinitionsMissingWarned;

        private class ConfigData
        {
            [JsonProperty("Plugin settings / Настройки плагина")]
            public GeneralSettings General = new GeneralSettings();

            [JsonProperty("Map drop settings / Настройки выпадения карт")]
            public DropSettings Drops = new DropSettings();

            [JsonProperty("Treasure spawn settings / Настройки появления сокровищ")]
            public SpawnSettings Spawn = new SpawnSettings();

            [JsonProperty("Chest settings / Настройки сундука")]
            public ChestSettings Chest = new ChestSettings();

            [JsonProperty("Marker settings / Настройки маркеров")]
            public MarkerSettings Markers = new MarkerSettings();

            [JsonProperty("Map item text / Текст предмета-карты")]
            public MapTextSettings MapText = new MapTextSettings();

            [JsonProperty("Loot tables / Таблицы лута")]
            public LootTables Loot = new LootTables();

            [JsonProperty("Messages / Сообщения")]
            public MessageSettings Messages = new MessageSettings();
        }

        private class GeneralSettings
        {
            [JsonProperty("Language: ru, en or auto / Язык: ru, en или auto")]
            public string Language = "ru";

            [JsonProperty("Use plugin prefix in chat / Использовать префикс в чате")]
            public bool UsePrefix = false;

            [JsonProperty("Admin permission / Права администратора")]
            public string AdminPermission = DefaultAdminPermission;

            [JsonProperty("Server admins have access without permission / Админы сервера имеют доступ без permission")]
            public bool ServerAdminsHaveAccess = true;

            [JsonProperty("Main chat command / Основная чат-команда")]
            public string MainCommand = "treasure";

            [JsonProperty("Short chat command / Короткая чат-команда")]
            public string ShortCommand = "th";

            [JsonProperty("Log debug to console / Писать debug в консоль")]
            public bool Debug = false;
        }

        private class DropSettings
        {
            [JsonProperty("Enable map drops from crates / Включить выпадение карт из ящиков")]
            public bool Enabled = true;

            [JsonProperty("Only one drop roll per crate / Только одна попытка выпадения с одного ящика")]
            public bool OneRollPerCrate = true;

            [JsonProperty("Drop rules / Правила выпадения")]
            public List<ContainerDropRule> Rules = new List<ContainerDropRule>
            {
                new ContainerDropRule { ShortPrefabName = "crate_normal_2", TreasureType = "normal", Chance = 0.08f },
                new ContainerDropRule { ShortPrefabName = "crate_normal", TreasureType = "rare", Chance = 0.20f },
                new ContainerDropRule { ShortPrefabName = "crate_elite", TreasureType = "epic", Chance = 0.20f },
                new ContainerDropRule { ShortPrefabName = "cargocrate", TreasureType = "legendary", Chance = 0.20f },
                new ContainerDropRule { ShortPrefabName = "oilrigcrate", TreasureType = "legendary", Chance = 0.20f },
                new ContainerDropRule { ShortPrefabName = "hackable_crate", TreasureType = "legendary", Chance = 0.20f },
                new ContainerDropRule { ShortPrefabName = "codelockedhackablecrate", TreasureType = "legendary", Chance = 0.20f }
            };
        }

        private class ContainerDropRule
        {
            [JsonProperty("Enabled / Включено")]
            public bool Enabled = true;

            [JsonProperty("Container ShortPrefabName / ShortPrefabName ящика")]
            public string ShortPrefabName = "crate_normal_2";

            [JsonProperty("Match mode: exact, contains, startswith / Режим сравнения: exact, contains, startswith")]
            public string MatchMode = "exact";

            [JsonProperty("Treasure type: normal, rare, epic, legendary / Тип карты: normal, rare, epic, legendary")]
            public string TreasureType = "normal";

            [JsonProperty("Chance 0-1 or 0-100 / Шанс 0-1 или 0-100")]
            public float Chance = 0.10f;
        }

        private class SpawnSettings
        {
            [JsonProperty("Min distance from activation point / Мин. расстояние от точки активации")]
            public float MinTreasureDistance = 50f;

            [JsonProperty("Max distance from activation point / Макс. расстояние от точки активации")]
            public float MaxTreasureDistance = 200f;

            [JsonProperty("Position search attempts / Попыток поиска позиции")]
            public int Attempts = 250;

            [JsonProperty("Lift chest above ground / Поднять сундук над землей")]
            public float GroundLift = 0.5f;

            [JsonProperty("Max ground slope degrees / Макс. наклон земли в градусах")]
            public float MaxGroundSlope = 32f;

            [JsonProperty("Flat ground check radius / Радиус проверки ровной земли")]
            public float FlatCheckRadius = 2f;

            [JsonProperty("Max height difference in flat check / Макс. перепад высоты при проверке ровной земли")]
            public float MaxGroundHeightDifference = 0.6f;

            [JsonProperty("Min distance to monuments / Мин. дистанция до монументов")]
            public float MinMonumentDistance = 80f;

            [JsonProperty("Min distance to player buildings / Мин. дистанция до построек игроков")]
            public float MinBuildingDistance = 30f;

            [JsonProperty("Obstacle check radius / Радиус проверки препятствий")]
            public float ObstacleCheckRadius = 3f;

            [JsonProperty("Do not spawn in water / Не спавнить в воде")]
            public bool AvoidWater = true;

            [JsonProperty("Minimum distance from water in meters / Мин. расстояние от воды в метрах")]
            public float MinDistanceFromWater = 25f;

            [JsonProperty("Water check step in meters / Шаг проверки воды в метрах")]
            public float WaterCheckStep = 5f;

            [JsonProperty("Water height padding / Запас высоты над водой")]
            public float WaterHeightPadding = 0.75f;

            [JsonProperty("Spawn only on terrain, not on world rocks/props / Спавнить только на terrain, не на скалах/World объектах")]
            public bool TerrainOnly = true;

            [JsonProperty("Reject ocean/lake/river topology / Запрещать зоны Ocean/Lake/River по topology")]
            public bool RejectWaterTopology = true;

            [JsonProperty("Do not spawn on rocks, trees, buildings and props / Не спавнить на скалах, деревьях, постройках и пропах")]
            public bool AvoidObstacles = true;
        }

        private class ChestSettings
        {
            [JsonProperty("Chest prefab / Префаб сундука")]
            public string Prefab = "assets/prefabs/deployable/small stash/small_stash_deployed.prefab";

            [JsonProperty("Inventory slots / Слоты инвентаря")]
            public int InventorySlots = 18;

            [JsonProperty("Chest lifetime in minutes / Время жизни сундука в минутах")]
            public float LifetimeMinutes = 10f;

            [JsonProperty("Remove chest after lifetime even if loot remains / Удалять сундук по таймеру даже если лут остался")]
            public bool RemoveAfterLifetime = true;

            [JsonProperty("Remove chest when empty / Удалять сундук когда он пустой")]
            public bool RemoveWhenEmpty = true;

            [JsonProperty("Maximum active chests per player / Макс. активных сундуков на игрока")]
            public int MaxActiveChestsPerPlayer = 3;

            [JsonProperty("Chest names RU / Названия сундуков RU")]
            public Dictionary<string, string> ChestNamesRu = new Dictionary<string, string>
            {
                { "normal", "Обычный сундук" },
                { "rare", "Редкий сундук" },
                { "epic", "Эпический сундук" },
                { "legendary", "Легендарный сундук" }
            };

            [JsonProperty("Chest names EN / Названия сундуков EN")]
            public Dictionary<string, string> ChestNamesEn = new Dictionary<string, string>
            {
                { "normal", "Common chest" },
                { "rare", "Rare chest" },
                { "epic", "Epic chest" },
                { "legendary", "Legendary chest" }
            };
        }

        private class MarkerSettings
        {
            [JsonProperty("Enable ddraw marker / Включить ddraw-маркер")]
            public bool Enabled = true;

            [JsonProperty("Marker duration seconds / Длительность маркера в секундах")]
            public float DurationSeconds = 600f;

            [JsonProperty("Marker text height / Высота текста маркера")]
            public float TextHeight = 2f;

            [JsonProperty("Ddraw text size / Размер ddraw текста")]
            public float DdrawTextSize = 14f;

            [JsonProperty("Marker box size / Размер box-маркера")]
            public float BoxSize = 3f;

            [JsonProperty("Clear old markers before redrawing / Очищать старые маркеры перед перерисовкой")]
            public bool ClearBeforeRedraw = true;

            [JsonProperty("Show ddraw text above chest / Показывать ddraw-текст над сундуком")]
            public bool DdrawTextEnabled = false;

            [JsonProperty("Show ddraw box around chest / Показывать ddraw-box вокруг сундука")]
            public bool DdrawBoxEnabled = false;

            [JsonProperty("Enable small CUI treasure HUD / Включить маленький CUI HUD сокровищ")]
            public bool CuiHudEnabled = true;

            [JsonProperty("CUI HUD refresh seconds / Обновление CUI HUD в секундах")]
            public float CuiHudRefreshSeconds = 0.5f;

            [JsonProperty("CUI HUD font size / Размер шрифта CUI HUD")]
            public int CuiHudFontSize = 12;

            [JsonProperty("CUI HUD max lines / Макс. строк в CUI HUD")]
            public int CuiHudMaxLines = 3;

            [JsonProperty("CUI HUD anchor min / CUI HUD anchor min")]
            public string CuiHudAnchorMin = "0.39 0.875";

            [JsonProperty("CUI HUD anchor max / CUI HUD anchor max")]
            public string CuiHudAnchorMax = "0.61 0.985";

            [JsonProperty("CUI HUD panel color / Цвет панели CUI HUD")]
            public string CuiHudPanelColor = "0 0 0 0.06";

            [JsonProperty("CUI HUD text color / Цвет текста CUI HUD")]
            public string CuiHudTextColor = "1 1 1 0.92";

            [JsonProperty("Colors by type / Цвета по типам")]
            public Dictionary<string, ColorConfig> Colors = new Dictionary<string, ColorConfig>
            {
                { "normal", new ColorConfig { R = 1f, G = 1f, B = 1f, A = 1f } },
                { "rare", new ColorConfig { R = 0.2f, G = 0.8f, B = 1f, A = 1f } },
                { "epic", new ColorConfig { R = 0.6f, G = 0.3f, B = 1f, A = 1f } },
                { "legendary", new ColorConfig { R = 1f, G = 0.6f, B = 0.1f, A = 1f } }
            };
        }

        private class ColorConfig
        {
            [JsonProperty("R / Красный")]
            public float R = 1f;

            [JsonProperty("G / Зеленый")]
            public float G = 1f;

            [JsonProperty("B / Синий")]
            public float B = 1f;

            [JsonProperty("A / Прозрачность")]
            public float A = 1f;
        }

        private class MapTextSettings
        {
            [JsonProperty("Map item shortname / Shortname предмета-карты")]
            public string MapItemShortname = "documents";

            [JsonProperty("Custom item parent shortname for blank vanilla description / Родитель кастомного предмета без ванильного описания")]
            public string CustomItemParentShortname = "documents";

            [JsonProperty("Accept old note maps / Принимать старые карты-note")]
            public bool AcceptLegacyNoteMaps = true;

            [JsonProperty("Show hidden technical id line in item text / Показывать техническую строку id в тексте предмета")]
            public bool ShowTechnicalIdLine = true;

            [JsonProperty("Map names RU / Названия карт RU")]
            public Dictionary<string, string> MapNamesRu = new Dictionary<string, string>
            {
                { "normal", "Обычная карта сокровищ" },
                { "rare", "Редкая карта сокровищ" },
                { "epic", "Эпическая карта сокровищ" },
                { "legendary", "Легендарная карта сокровищ" }
            };

            [JsonProperty("Map names EN / Названия карт EN")]
            public Dictionary<string, string> MapNamesEn = new Dictionary<string, string>
            {
                { "normal", "Common treasure map" },
                { "rare", "Rare treasure map" },
                { "epic", "Epic treasure map" },
                { "legendary", "Legendary treasure map" }
            };

            [JsonProperty("IMPORTANT: Custom item note / ВАЖНО: заметка по кастомному предмету")]
            public string ResearchPaperDescriptionNotice = "For a real custom item the plugin uses CustomItemDefinitions. Default parent is documents.";

            [JsonProperty("Try override vanilla inventory description / Пытаться заменить ванильное описание в инвентаре")]
            public bool TryOverrideInventoryDescription = false;

            [JsonProperty("Use CustomItemDefinitions for real inventory description / Использовать CustomItemDefinitions для настоящего описания в инвентаре")]
            public bool UseCustomItemDefinitions = true;

            [JsonProperty("Custom item shortname prefix / Префикс shortname кастомных карт")]
            public string CustomItemShortnamePrefix = "treasurehunt.map.v24.";

            [JsonProperty("Default map skin ID if rarity skin is 0 / Общий Skin ID карты если для редкости 0")]
            public ulong MapSkinId = 1357238701UL;

            [JsonProperty("Map skin IDs by rarity / Skin ID карт по редкости")]
            public Dictionary<string, ulong> MapSkinIds = new Dictionary<string, ulong>
            {
                { "normal", 1357238701UL },
                { "rare", 1278549155UL },
                { "epic", 914593147UL },
                { "legendary", 931374018UL }
            };

            [JsonProperty("Show CustomItemDefinitions tooltip block / Показывать блок описания через CustomItemDefinitions")]
            public bool ShowCustomTooltipBlock = true;

            [JsonProperty("Tooltip block label RU / Заголовок блока описания RU")]
            public string TooltipBlockLabelRu = "КАК ИСПОЛЬЗОВАТЬ";

            [JsonProperty("Tooltip block label EN / Заголовок блока описания EN")]
            public string TooltipBlockLabelEn = "HOW TO USE";

            [JsonProperty("Show warning if CustomItemDefinitions is missing / Показывать предупреждение если нет CustomItemDefinitions")]
            public bool WarnIfCustomItemDefinitionsMissing = true;

            [JsonProperty("Generic inventory description RU / Общее описание в инвентаре RU")]
            public string InventoryDescriptionRu = "Как использовать?\nПоложите эту карту сокровищ в исследовательский стол и осмотритесь вокруг.";

            [JsonProperty("Generic inventory description EN / Общее описание в инвентаре EN")]
            public string InventoryDescriptionEn = "How to use?\nPlace this treasure map into a research table and look around.";

            [JsonProperty("Map descriptions RU / Описания карт RU")]
            public Dictionary<string, string> MapDescriptionsRu = new Dictionary<string, string>
            {
                { "normal", "Как использовать?\nПоложите эту обычную карту сокровищ в исследовательский стол и осмотритесь вокруг." },
                { "rare", "Как использовать?\nПоложите эту редкую карту сокровищ в исследовательский стол и осмотритесь вокруг." },
                { "epic", "Как использовать?\nПоложите эту эпическую карту сокровищ в исследовательский стол и осмотритесь вокруг." },
                { "legendary", "Как использовать?\nПоложите эту легендарную карту сокровищ в исследовательский стол и осмотритесь вокруг." }
            };

            [JsonProperty("Map descriptions EN / Описания карт EN")]
            public Dictionary<string, string> MapDescriptionsEn = new Dictionary<string, string>
            {
                { "normal", "How to use?\nPlace this common treasure map into a research table and look around." },
                { "rare", "How to use?\nPlace this rare treasure map into a research table and look around." },
                { "epic", "How to use?\nPlace this epic treasure map into a research table and look around." },
                { "legendary", "How to use?\nPlace this legendary treasure map into a research table and look around." }
            };

            [JsonProperty("Legacy note body RU / Старый текст записки RU")]
            public string NoteBodyRu = "Положите эту карту в исследовательский стол, чтобы найти сокровища.";

            [JsonProperty("Legacy note body EN / Старый текст записки EN")]
            public string NoteBodyEn = "Place this map into a research table to reveal a treasure chest.";

            [JsonProperty("Rarity names RU / Названия редкости RU")]
            public Dictionary<string, string> RarityNamesRu = new Dictionary<string, string>
            {
                { "normal", "обычный" },
                { "rare", "редкий" },
                { "epic", "эпический" },
                { "legendary", "легендарный" }
            };

            [JsonProperty("Rarity names EN / Названия редкости EN")]
            public Dictionary<string, string> RarityNamesEn = new Dictionary<string, string>
            {
                { "normal", "common" },
                { "rare", "rare" },
                { "epic", "epic" },
                { "legendary", "legendary" }
            };
        }

        private class LootTables
        {
            [JsonProperty("Normal / Обычный")]
            public LootProfile Normal = new LootProfile
            {
                MinItems = 4,
                MaxItems = 6,
                WeaponRollChance = 0.25f,
                Items = new List<LootItem>
                {
                    new LootItem { Shortname = "scrap", Min = 50, Max = 75, Chance = 1.00f },
                    new LootItem { Shortname = "stones", Min = 500, Max = 3000, Chance = 0.80f },
                    new LootItem { Shortname = "wood", Min = 500, Max = 1500, Chance = 0.70f },
                    new LootItem { Shortname = "cloth", Min = 100, Max = 200, Chance = 0.70f },
                    new LootItem { Shortname = "metal.fragments", Min = 1000, Max = 2000, Chance = 0.80f },
                    new LootItem { Shortname = "metal.refined", Min = 10, Max = 25, Chance = 0.50f },
                    new LootItem { Shortname = "lowgradefuel", Min = 100, Max = 150, Chance = 0.75f },
                    new LootItem { Shortname = "gunpowder", Min = 100, Max = 250, Chance = 0.70f },
                    new LootItem { Shortname = "gears", Min = 1, Max = 3, Chance = 0.40f },
                    new LootItem { Shortname = "rope", Min = 2, Max = 5, Chance = 0.80f },
                    new LootItem { Shortname = "metalblade", Min = 1, Max = 4, Chance = 0.80f },
                    new LootItem { Shortname = "fuse", Min = 1, Max = 2, Chance = 0.40f },
                    new LootItem { Shortname = "roadsigns", Min = 1, Max = 2, Chance = 0.50f },
                    new LootItem { Shortname = "charcoal", Min = 1000, Max = 3000, Chance = 0.70f },
                    new LootItem { Shortname = "tarp", Min = 1, Max = 2, Chance = 0.60f },
                    new LootItem { Shortname = "syringe.medical", Min = 1, Max = 2, Chance = 0.30f }
                },
                Weapons = new List<LootItem>
                {
                    new LootItem { Shortname = "pistol.revolver", Min = 1, Max = 1, Chance = 1f },
                    new LootItem { Shortname = "shotgun.double", Min = 1, Max = 1, Chance = 1f }
                }
            };

            [JsonProperty("Rare / Редкий")]
            public LootProfile Rare = new LootProfile
            {
                MinItems = 5,
                MaxItems = 8,
                WeaponRollChance = 0.50f,
                Items = new List<LootItem>
                {
                    new LootItem { Shortname = "scrap", Min = 120, Max = 250, Chance = 1.00f },
                    new LootItem { Shortname = "metal.refined", Min = 30, Max = 60, Chance = 0.90f },
                    new LootItem { Shortname = "gunpowder", Min = 250, Max = 400, Chance = 0.90f },
                    new LootItem { Shortname = "techparts", Min = 1, Max = 2, Chance = 0.60f },
                    new LootItem { Shortname = "furnace.large", Min = 1, Max = 1, Chance = 0.25f },
                    new LootItem { Shortname = "refinery_small", Min = 1, Max = 1, Chance = 0.25f },
                    new LootItem { Shortname = "chainsaw", Min = 1, Max = 1, Chance = 0.30f },
                    new LootItem { Shortname = "jackhammer", Min = 1, Max = 1, Chance = 0.30f },
                    new LootItem { Shortname = "roadsign.jacket", Min = 1, Max = 1, Chance = 0.40f },
                    new LootItem { Shortname = "roadsign.kilt", Min = 1, Max = 1, Chance = 0.40f },
                    new LootItem { Shortname = "roadsign.gloves", Min = 1, Max = 1, Chance = 0.35f },
                    new LootItem { Shortname = "roadsign.helmet", Min = 1, Max = 1, Chance = 0.35f },
                    new LootItem { Shortname = "metalpipe", Min = 2, Max = 4, Chance = 0.70f },
                    new LootItem { Shortname = "gears", Min = 2, Max = 4, Chance = 0.60f },
                    new LootItem { Shortname = "metalblade", Min = 2, Max = 5, Chance = 0.80f },
                    new LootItem { Shortname = "rope", Min = 2, Max = 5, Chance = 0.70f },
                    new LootItem { Shortname = "fuse", Min = 1, Max = 2, Chance = 0.50f }
                },
                Weapons = new List<LootItem>
                {
                    new LootItem { Shortname = "smg.thompson", Min = 1, Max = 1, Chance = 1f },
                    new LootItem { Shortname = "pistol.python", Min = 1, Max = 1, Chance = 1f },
                    new LootItem { Shortname = "shotgun.pump", Min = 1, Max = 1, Chance = 1f }
                }
            };

            [JsonProperty("Epic / Эпический")]
            public LootProfile Epic = new LootProfile
            {
                MinItems = 6,
                MaxItems = 9,
                WeaponRollChance = 0.60f,
                Items = new List<LootItem>
                {
                    new LootItem { Shortname = "scrap", Min = 200, Max = 300, Chance = 0.80f },
                    new LootItem { Shortname = "metal.refined", Min = 60, Max = 100, Chance = 0.80f },
                    new LootItem { Shortname = "explosives", Min = 2, Max = 5, Chance = 0.75f },
                    new LootItem { Shortname = "techparts", Min = 2, Max = 4, Chance = 0.80f },
                    new LootItem { Shortname = "gunpowder", Min = 400, Max = 700, Chance = 0.80f },
                    new LootItem { Shortname = "riflebody", Min = 1, Max = 2, Chance = 0.50f },
                    new LootItem { Shortname = "metalpipe", Min = 2, Max = 4, Chance = 0.50f },
                    new LootItem { Shortname = "hazmatsuit", Min = 1, Max = 1, Chance = 0.50f },
                    new LootItem { Shortname = "smg.mp5", Min = 1, Max = 1, Chance = 0.30f },
                    new LootItem { Shortname = "shotgun.spas12", Min = 1, Max = 1, Chance = 0.30f },
                    new LootItem { Shortname = "ammo.rocket.hv", Min = 1, Max = 2, Chance = 0.25f },
                    new LootItem { Shortname = "autoturret", Min = 1, Max = 1, Chance = 0.15f },
                    new LootItem { Shortname = "targeting.computer", Min = 1, Max = 1, Chance = 0.20f },
                    new LootItem { Shortname = "techtrash", Min = 1, Max = 3, Chance = 0.80f },
                    new LootItem { Shortname = "grenade.beancan", Min = 1, Max = 3, Chance = 0.40f },
                    new LootItem { Shortname = "ammo.rifle.explosive", Min = 10, Max = 16, Chance = 0.25f }
                },
                Weapons = new List<LootItem>
                {
                    new LootItem { Shortname = "smg.mp5", Min = 1, Max = 1, Chance = 1f },
                    new LootItem { Shortname = "shotgun.spas12", Min = 1, Max = 1, Chance = 1f },
                    new LootItem { Shortname = "rifle.semiauto", Min = 1, Max = 1, Chance = 1f },
                    new LootItem { Shortname = "smg.thompson", Min = 1, Max = 1, Chance = 1f }
                }
            };

            [JsonProperty("Legendary / Легендарный")]
            public LootProfile Legendary = new LootProfile
            {
                MinItems = 7,
                MaxItems = 9,
                WeaponRollChance = 0.55f,
                Items = new List<LootItem>
                {
                    new LootItem { Shortname = "scrap", Min = 280, Max = 420, Chance = 0.85f },
                    new LootItem { Shortname = "metal.refined", Min = 90, Max = 160, Chance = 0.80f },
                    new LootItem { Shortname = "explosives", Min = 3, Max = 5, Chance = 0.75f },
                    new LootItem { Shortname = "techparts", Min = 3, Max = 5, Chance = 0.80f },
                    new LootItem { Shortname = "cctv.camera", Min = 1, Max = 1, Chance = 0.55f },
                    new LootItem { Shortname = "targeting.computer", Min = 1, Max = 1, Chance = 0.55f },
                    new LootItem { Shortname = "metal.facemask", Min = 1, Max = 1, Chance = 0.35f },
                    new LootItem { Shortname = "metal.plate.torso", Min = 1, Max = 1, Chance = 0.25f },
                    new LootItem { Shortname = "rifle.ak", Min = 1, Max = 1, Chance = 0.22f },
                    new LootItem { Shortname = "rifle.lr300", Min = 1, Max = 1, Chance = 0.16f },
                    new LootItem { Shortname = "lmg.m249", Min = 1, Max = 1, Chance = 0.04f },
                    new LootItem { Shortname = "autoturret", Min = 1, Max = 1, Chance = 0.18f },
                    new LootItem { Shortname = "ammo.rocket.basic", Min = 1, Max = 2, Chance = 0.20f },
                    new LootItem { Shortname = "supply.signal", Min = 1, Max = 1, Chance = 0.25f },
                    new LootItem { Shortname = "rifle.bolt", Min = 1, Max = 1, Chance = 0.20f },
                    new LootItem { Shortname = "techtrash", Min = 2, Max = 4, Chance = 0.75f },
                    new LootItem { Shortname = "weapon.mod.8x.scope", Min = 1, Max = 1, Chance = 0.15f },
                    new LootItem { Shortname = "weapon.mod.silencer", Min = 1, Max = 1, Chance = 0.25f },
                    new LootItem { Shortname = "weapon.mod.lasersight", Min = 1, Max = 1, Chance = 0.25f }
                },
                Weapons = new List<LootItem>
                {
                    new LootItem { Shortname = "rifle.ak", Min = 1, Max = 1, Chance = 1f },
                    new LootItem { Shortname = "rifle.lr300", Min = 1, Max = 1, Chance = 1f },
                    new LootItem { Shortname = "lmg.m249", Min = 1, Max = 1, Chance = 0.08f },
                    new LootItem { Shortname = "rifle.bolt", Min = 1, Max = 1, Chance = 1f }
                }
            };
        }

        private class LootProfile
        {
            [JsonProperty("Minimum items / Мин. предметов")]
            public int MinItems = 3;

            [JsonProperty("Maximum items / Макс. предметов")]
            public int MaxItems = 6;

            [JsonProperty("Roll weapon separately / Отдельно роллить оружие")]
            public bool RollWeaponSeparately = true;

            [JsonProperty("Weapon roll chance 0-1 or 0-100 / Шанс оружия 0-1 или 0-100")]
            public float WeaponRollChance = 0.25f;

            [JsonProperty("Loot items / Предметы лута")]
            public List<LootItem> Items = new List<LootItem>();

            [JsonProperty("Weapon items / Предметы оружия")]
            public List<LootItem> Weapons = new List<LootItem>();
        }

        private class LootItem
        {
            [JsonProperty("Shortname / Shortname")]
            public string Shortname = "scrap";

            [JsonProperty("Minimum amount / Мин. количество")]
            public int Min = 1;

            [JsonProperty("Maximum amount / Макс. количество")]
            public int Max = 1;

            [JsonProperty("Chance 0-1 or 0-100 / Шанс 0-1 или 0-100")]
            public float Chance = 1f;
        }

        private class MessageSettings
        {
            [JsonProperty("RU / Русский")]
            public Dictionary<string, string> Ru = new Dictionary<string, string>
            {
                { "Prefix", "<color=#ffb300>[Treasure Hunt]</color>" },
                { "NoPermission", "<color=white>Недостаточно прав.</color>" },
                { "Help", "<color=white>Команды: /{0} help, /{0} clearhud, /{0} hud. Админ: /{0} ui, /{0} give normal|rare|epic|legendary, /{0} name ru|en normal|rare|epic|legendary текст, /{0} clear, /{0} clearall.</color>" },
                { "MapFound", "<color=white>Вы нашли: <color=#ffb300>{0}</color>.</color>" },
                { "MapGiven", "<color=white>Карта выдана: <color=#ffb300>{0}</color>.</color>" },
                { "InvalidMapType", "<color=white>Неверный тип карты. Используйте: normal, rare, epic, legendary.</color>" },
                { "ChestCreated", "<color=white>Сокровища найдены! Ищите <color=#ffb300>{0}</color>.</color>" },
                { "ChestLooted", "<color=white>{0} был залутан и исчез.</color>" },
                { "ChestExpired", "<color=white>Сундук сокровищ исчез по таймеру.</color>" },
                { "NoActiveChests", "<color=white>У вас нет активных сокровищ.</color>" },
                { "TooManyChests", "<color=white>У вас уже максимум активных сундуков: {0}.</color>" },
                { "PlayerChestsCleared", "<color=white>Ваши активные сундуки очищены.</color>" },
                { "AllChestsCleared", "<color=white>Все активные сундуки Treasure Hunt очищены.</color>" },
                { "MarkersCleared", "<color=white>Маркеры сокровищ очищены.</color>" },
                { "PositionFailed", "<color=white>Не удалось найти безопасное место для сокровищ. Попробуйте ещё раз.</color>" },
                { "MarkerText", "{0}" },
                { "NameCommandHelp", "<color=white>Использование: /{0} name ru|en normal|rare|epic|legendary Новое название</color>" },
                { "NameUpdated", "<color=white>Название обновлено: <color=#ffb300>{0}</color> = <color=#ffb300>{1}</color>.</color>" },
                { "HudShown", "<color=white>CUI HUD сокровищ включен для вас.</color>" },
                { "HudHidden", "<color=white>Маркеры и CUI HUD скрыты. Используйте /{0} hud, чтобы включить HUD снова.</color>" }
            };

            [JsonProperty("EN / Английский")]
            public Dictionary<string, string> En = new Dictionary<string, string>
            {
                { "Prefix", "<color=#ffb300>[Treasure Hunt]</color>" },
                { "NoPermission", "<color=white>You do not have permission.</color>" },
                { "Help", "<color=white>Commands: /{0} help, /{0} clearhud, /{0} hud. Admin: /{0} ui, /{0} give normal|rare|epic|legendary, /{0} name ru|en normal|rare|epic|legendary text, /{0} clear, /{0} clearall.</color>" },
                { "MapFound", "<color=white>You found: <color=#ffb300>{0}</color>.</color>" },
                { "MapGiven", "<color=white>Map given: <color=#ffb300>{0}</color>.</color>" },
                { "InvalidMapType", "<color=white>Invalid map type. Use: normal, rare, epic, legendary.</color>" },
                { "ChestCreated", "<color=white>Treasure revealed! Find the <color=#ffb300>{0}</color>.</color>" },
                { "ChestLooted", "<color=white>The {0} was looted and disappeared.</color>" },
                { "ChestExpired", "<color=white>The treasure chest expired.</color>" },
                { "NoActiveChests", "<color=white>You have no active treasures.</color>" },
                { "TooManyChests", "<color=white>You already have the maximum active chests: {0}.</color>" },
                { "PlayerChestsCleared", "<color=white>Your active treasure chests were cleared.</color>" },
                { "AllChestsCleared", "<color=white>All active Treasure Hunt chests were cleared.</color>" },
                { "MarkersCleared", "<color=white>Treasure markers were cleared.</color>" },
                { "PositionFailed", "<color=white>Could not find a safe treasure position. Try again.</color>" },
                { "MarkerText", "{0}" },
                { "NameCommandHelp", "<color=white>Usage: /{0} name ru|en normal|rare|epic|legendary New name</color>" },
                { "NameUpdated", "<color=white>Name updated: <color=#ffb300>{0}</color> = <color=#ffb300>{1}</color>.</color>" },
                { "HudShown", "<color=white>Treasure CUI HUD enabled for you.</color>" },
                { "HudHidden", "<color=white>Markers and CUI HUD hidden. Use /{0} hud to show the HUD again.</color>" }
            };
        }

        protected override void LoadDefaultConfig()
        {
            config = new ConfigData();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<ConfigData>();
                if (config == null)
                    throw new Exception("Config is null");

                ValidateConfig();
                SaveConfig();
            }
            catch (Exception ex)
            {
                PrintError("Config load failed. Creating a new default config. Error: " + ex.Message);
                LoadDefaultConfig();
                SaveConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void ValidateConfig()
        {
            if (config.General == null) config.General = new GeneralSettings();
            if (string.IsNullOrEmpty(config.General.AdminPermission)) config.General.AdminPermission = DefaultAdminPermission;
            if (string.IsNullOrEmpty(config.General.MainCommand)) config.General.MainCommand = "treasure";
            if (string.IsNullOrEmpty(config.General.ShortCommand)) config.General.ShortCommand = "th";
            if (string.IsNullOrEmpty(config.General.Language)) config.General.Language = "ru";
            config.General.UsePrefix = false;

            if (config.Drops == null) config.Drops = new DropSettings();
            if (config.Drops.Rules == null) config.Drops.Rules = new List<ContainerDropRule>();

            if (config.Spawn == null) config.Spawn = new SpawnSettings();
            if (config.Spawn.Attempts < 1) config.Spawn.Attempts = 1;
            if (config.Spawn.MinTreasureDistance < 0f) config.Spawn.MinTreasureDistance = 0f;
            if (config.Spawn.MaxTreasureDistance < config.Spawn.MinTreasureDistance)
                config.Spawn.MaxTreasureDistance = config.Spawn.MinTreasureDistance + 1f;
            if (config.Spawn.GroundLift < 0.35f) config.Spawn.GroundLift = 0.5f;
            if (config.Spawn.MaxGroundSlope <= 0f || config.Spawn.MaxGroundSlope > 60f) config.Spawn.MaxGroundSlope = 32f;
            if (config.Spawn.FlatCheckRadius < 0.5f) config.Spawn.FlatCheckRadius = 2f;
            if (config.Spawn.MaxGroundHeightDifference < 0.1f) config.Spawn.MaxGroundHeightDifference = 0.6f;
            if (config.Spawn.MinDistanceFromWater < 0f) config.Spawn.MinDistanceFromWater = 0f;
            if (config.Spawn.WaterCheckStep < 1f) config.Spawn.WaterCheckStep = 1f;
            if (config.Spawn.WaterHeightPadding < 0f) config.Spawn.WaterHeightPadding = 0f;

            if (config.Chest == null) config.Chest = new ChestSettings();
            if (string.IsNullOrEmpty(config.Chest.Prefab)) config.Chest.Prefab = "assets/prefabs/deployable/small stash/small_stash_deployed.prefab";
            if (config.Chest.InventorySlots < 1) config.Chest.InventorySlots = 1;
            if (config.Chest.LifetimeMinutes < 0f) config.Chest.LifetimeMinutes = 0f;
            if (config.Chest.MaxActiveChestsPerPlayer < 1) config.Chest.MaxActiveChestsPerPlayer = 1;
            if (config.Chest.ChestNamesRu == null) config.Chest.ChestNamesRu = new Dictionary<string, string>();
            if (config.Chest.ChestNamesEn == null) config.Chest.ChestNamesEn = new Dictionary<string, string>();
            EnsureChestNameDefaults();

            if (config.Markers == null) config.Markers = new MarkerSettings();
            if (config.Markers.Colors == null) config.Markers.Colors = new Dictionary<string, ColorConfig>();
            if (config.Markers.TextHeight < 0.1f) config.Markers.TextHeight = 0.1f;
            if (config.Markers.DdrawTextSize <= 5f)
            {
                float oldScale = Mathf.InverseLerp(0.5f, 5f, Mathf.Clamp(config.Markers.DdrawTextSize, 0.5f, 5f));
                config.Markers.DdrawTextSize = Mathf.RoundToInt(Mathf.Lerp(8f, 30f, oldScale));
            }
            if (config.Markers.DdrawTextSize < 6f) config.Markers.DdrawTextSize = 6f;
            if (config.Markers.DdrawTextSize > 40f) config.Markers.DdrawTextSize = 40f;
            if (config.Markers.BoxSize < 0.1f) config.Markers.BoxSize = 0.1f;
            if (config.Markers.CuiHudRefreshSeconds <= 0f || config.Markers.CuiHudRefreshSeconds > 1f) config.Markers.CuiHudRefreshSeconds = 0.5f;
            if (config.Markers.CuiHudRefreshSeconds < 0.25f) config.Markers.CuiHudRefreshSeconds = 0.25f;
            if (config.Markers.CuiHudFontSize < 8) config.Markers.CuiHudFontSize = 8;
            if (config.Markers.CuiHudMaxLines < 1) config.Markers.CuiHudMaxLines = 1;
            if (config.Markers.CuiHudFontSize == 11) config.Markers.CuiHudFontSize = 12;
            if (config.Markers.CuiHudMaxLines > 3) config.Markers.CuiHudMaxLines = 3;
            if (string.IsNullOrEmpty(config.Markers.CuiHudAnchorMin) || config.Markers.CuiHudAnchorMin == "0.36 0.925" || config.Markers.CuiHudAnchorMin == "0.39 0.935") config.Markers.CuiHudAnchorMin = "0.39 0.875";
            if (string.IsNullOrEmpty(config.Markers.CuiHudAnchorMax) || config.Markers.CuiHudAnchorMax == "0.64 0.985") config.Markers.CuiHudAnchorMax = "0.61 0.985";
            if (string.IsNullOrEmpty(config.Markers.CuiHudPanelColor) || config.Markers.CuiHudPanelColor == "0.04 0.04 0.04 0.55") config.Markers.CuiHudPanelColor = "0 0 0 0.06";
            if (string.IsNullOrEmpty(config.Markers.CuiHudTextColor) || config.Markers.CuiHudTextColor == "1 1 1 0.95") config.Markers.CuiHudTextColor = "1 1 1 0.92";

            if (config.MapText == null) config.MapText = new MapTextSettings();
            // базовый предмет карты - documents, иконка задаётся скином
            if (string.IsNullOrEmpty(config.MapText.MapItemShortname)
                || string.Equals(config.MapText.MapItemShortname.Trim(), "scrap", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.MapItemShortname.Trim(), "researchpaper", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.MapItemShortname.Trim(), "map", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.MapItemShortname.Trim(), "rug", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.MapItemShortname.Trim(), "rowboat", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.MapItemShortname.Trim(), "rhib", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.MapItemShortname.Trim(), "keycard_green", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.MapItemShortname.Trim(), "keycard_blue", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.MapItemShortname.Trim(), "spraycandecal", StringComparison.OrdinalIgnoreCase))
                config.MapText.MapItemShortname = "documents";
            if (string.IsNullOrEmpty(config.MapText.CustomItemParentShortname)
                || string.Equals(config.MapText.CustomItemParentShortname.Trim(), "scrap", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemParentShortname.Trim(), "researchpaper", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemParentShortname.Trim(), "map", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemParentShortname.Trim(), "rug", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemParentShortname.Trim(), "rowboat", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemParentShortname.Trim(), "rhib", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemParentShortname.Trim(), "keycard_green", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemParentShortname.Trim(), "keycard_blue", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemParentShortname.Trim(), "spraycandecal", StringComparison.OrdinalIgnoreCase))
                config.MapText.CustomItemParentShortname = "documents";
            if (config.MapText.MapNamesRu == null) config.MapText.MapNamesRu = new Dictionary<string, string>();
            if (config.MapText.MapNamesEn == null) config.MapText.MapNamesEn = new Dictionary<string, string>();
            if (config.MapText.MapDescriptionsRu == null) config.MapText.MapDescriptionsRu = new Dictionary<string, string>();
            if (config.MapText.MapDescriptionsEn == null) config.MapText.MapDescriptionsEn = new Dictionary<string, string>();
            if (config.MapText.RarityNamesRu == null) config.MapText.RarityNamesRu = new Dictionary<string, string>();
            if (config.MapText.RarityNamesEn == null) config.MapText.RarityNamesEn = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(config.MapText.InventoryDescriptionRu)) config.MapText.InventoryDescriptionRu = "Как использовать?\nПоложите эту карту сокровищ в исследовательский стол и осмотритесь вокруг.";
            if (string.IsNullOrEmpty(config.MapText.InventoryDescriptionEn)) config.MapText.InventoryDescriptionEn = "How to use?\nPlace this treasure map into a research table and look around.";
            if (config.MapText.InventoryDescriptionRu == "Карта сокровищ. Положите её в исследовательский стол, чтобы открыть местоположение сундука.")
                config.MapText.InventoryDescriptionRu = "Как использовать?\nПоложите эту карту сокровищ в исследовательский стол и осмотритесь вокруг.";
            if (config.MapText.InventoryDescriptionEn == "Treasure map. Place it into a research table to reveal a treasure chest.")
                config.MapText.InventoryDescriptionEn = "How to use?\nPlace this treasure map into a research table and look around.";
            config.MapText.TryOverrideInventoryDescription = false;
            if (config.MapText.MapSkinIds == null) config.MapText.MapSkinIds = new Dictionary<string, ulong>();
            SetMapSkin("normal", 1357238701UL);      // Treasure Map
            SetMapSkin("rare", 1278549155UL);        // Vintage Map Rug
            SetMapSkin("epic", 914593147UL);         // Compass Rug
            SetMapSkin("legendary", 931374018UL);    // Base Plan Rug
            if (string.IsNullOrEmpty(config.MapText.CustomItemShortnamePrefix)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.v2.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.v3.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.v4.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.v5.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.v6.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.v7.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.v8.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.v9.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.v10.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.v11.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.v12.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.v13.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.v14.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.v15.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.v16.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.v17.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.v18.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.v19.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.v20.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.v21.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.v22.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(config.MapText.CustomItemShortnamePrefix.Trim(), "treasurehunt.map.v23.", StringComparison.OrdinalIgnoreCase))
                config.MapText.CustomItemShortnamePrefix = "treasurehunt.map.v24.";
            if (string.IsNullOrEmpty(config.MapText.TooltipBlockLabelRu)
                || config.MapText.TooltipBlockLabelRu == "ОПИСАНИЕ ПРЕДМЕТА"
                || config.MapText.TooltipBlockLabelRu == "КАК ИСПОЛЬЗОВАТЬ?")
                config.MapText.TooltipBlockLabelRu = "КАК ИСПОЛЬЗОВАТЬ";
            if (string.IsNullOrEmpty(config.MapText.TooltipBlockLabelEn)
                || config.MapText.TooltipBlockLabelEn == "ITEM DESCRIPTION"
                || config.MapText.TooltipBlockLabelEn == "HOW TO USE?")
                config.MapText.TooltipBlockLabelEn = "HOW TO USE";
            // предметы регистрируем через CustomItemDefinitions, без ванильного описания
            config.MapText.ShowCustomTooltipBlock = true;
            EnsureMapTextDefaults();

            if (config.Loot == null) config.Loot = new LootTables();
            EnsureLootProfile(config.Loot.Normal);
            EnsureLootProfile(config.Loot.Rare);
            EnsureLootProfile(config.Loot.Epic);
            EnsureLootProfile(config.Loot.Legendary);
            BalanceLegendaryLoot(config.Loot.Legendary);
            RemoveLootShortname(config.Loot.Normal, "ducttape");
            RemoveLootShortname(config.Loot.Rare, "ducttape");
            RemoveLootShortname(config.Loot.Epic, "ducttape");
            RemoveLootShortname(config.Loot.Legendary, "ducttape");

            if (config.Messages == null) config.Messages = new MessageSettings();
            if (config.Messages.Ru == null) config.Messages.Ru = new Dictionary<string, string>();
            if (config.Messages.En == null) config.Messages.En = new Dictionary<string, string>();
            EnsureMessageDefaults();
            FixLegacyMessageTexts();
        }

        private void EnsureLootProfile(LootProfile profile)
        {
            if (profile == null) return;
            if (profile.Items == null) profile.Items = new List<LootItem>();
            if (profile.Weapons == null) profile.Weapons = new List<LootItem>();
            if (profile.MinItems < 0) profile.MinItems = 0;
            if (profile.MaxItems < profile.MinItems) profile.MaxItems = profile.MinItems;
        }

        private void BalanceLegendaryLoot(LootProfile profile)
        {
            if (profile == null) return;
            if (profile.Items == null) profile.Items = new List<LootItem>();
            if (profile.Weapons == null) profile.Weapons = new List<LootItem>();

            profile.MinItems = 7;
            profile.MaxItems = 9;
            profile.WeaponRollChance = 0.55f;

            SetLootItem(profile.Items, "scrap", 280, 420, 0.85f);
            SetLootItem(profile.Items, "metal.refined", 90, 160, 0.80f);
            SetLootItem(profile.Items, "explosives", 3, 5, 0.75f);
            SetLootItem(profile.Items, "techparts", 3, 5, 0.80f);
            SetLootItem(profile.Items, "cctv.camera", 1, 1, 0.55f);
            SetLootItem(profile.Items, "targeting.computer", 1, 1, 0.55f);
            SetLootItem(profile.Items, "metal.facemask", 1, 1, 0.35f);
            SetLootItem(profile.Items, "metal.plate.torso", 1, 1, 0.25f);
            SetLootItem(profile.Items, "rifle.ak", 1, 1, 0.22f);
            SetLootItem(profile.Items, "rifle.lr300", 1, 1, 0.16f);
            SetLootItem(profile.Items, "lmg.m249", 1, 1, 0.04f);
            SetLootItem(profile.Items, "autoturret", 1, 1, 0.18f);
            SetLootItem(profile.Items, "ammo.rocket.basic", 1, 2, 0.20f);
            SetLootItem(profile.Items, "supply.signal", 1, 1, 0.25f);
            SetLootItem(profile.Items, "rifle.bolt", 1, 1, 0.20f);
            SetLootItem(profile.Items, "techtrash", 2, 4, 0.75f);
            SetLootItem(profile.Items, "weapon.mod.8x.scope", 1, 1, 0.15f);
            SetLootItem(profile.Items, "weapon.mod.silencer", 1, 1, 0.25f);
            SetLootItem(profile.Items, "weapon.mod.lasersight", 1, 1, 0.25f);

            SetLootItem(profile.Weapons, "rifle.ak", 1, 1, 1f);
            SetLootItem(profile.Weapons, "rifle.lr300", 1, 1, 1f);
            SetLootItem(profile.Weapons, "lmg.m249", 1, 1, 0.08f);
            SetLootItem(profile.Weapons, "rifle.bolt", 1, 1, 1f);
        }

        private void SetLootItem(List<LootItem> list, string shortname, int min, int max, float chance)
        {
            if (list == null || string.IsNullOrEmpty(shortname)) return;

            LootItem item = null;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                LootItem current = list[i];
                if (current == null || string.IsNullOrEmpty(current.Shortname)) continue;
                if (!string.Equals(current.Shortname.Trim(), shortname, StringComparison.OrdinalIgnoreCase)) continue;

                if (item == null)
                    item = current;
                else
                    list.RemoveAt(i);
            }

            if (item == null)
            {
                item = new LootItem { Shortname = shortname };
                list.Add(item);
            }

            item.Shortname = shortname;
            item.Min = min;
            item.Max = Mathf.Max(min, max);
            item.Chance = chance;
        }

        private void RemoveLootShortname(LootProfile profile, string shortname)
        {
            if (profile == null || profile.Items == null || string.IsNullOrEmpty(shortname)) return;

            for (int i = profile.Items.Count - 1; i >= 0; i--)
            {
                LootItem item = profile.Items[i];
                if (item == null || string.IsNullOrEmpty(item.Shortname)) continue;

                if (string.Equals(item.Shortname.Trim(), shortname, StringComparison.OrdinalIgnoreCase))
                    profile.Items.RemoveAt(i);
            }
        }

        private bool IsBlockedLootShortname(string shortname)
        {
            if (string.IsNullOrEmpty(shortname)) return false;
            return string.Equals(shortname.Trim(), "ducttape", StringComparison.OrdinalIgnoreCase);
        }

        private void EnsureChestNameDefaults()
        {
            EnsureString(config.Chest.ChestNamesRu, "normal", "Обычный сундук");
            EnsureString(config.Chest.ChestNamesRu, "rare", "Редкий сундук");
            EnsureString(config.Chest.ChestNamesRu, "epic", "Эпический сундук");
            EnsureString(config.Chest.ChestNamesRu, "legendary", "Легендарный сундук");

            EnsureString(config.Chest.ChestNamesEn, "normal", "Common chest");
            EnsureString(config.Chest.ChestNamesEn, "rare", "Rare chest");
            EnsureString(config.Chest.ChestNamesEn, "epic", "Epic chest");
            EnsureString(config.Chest.ChestNamesEn, "legendary", "Legendary chest");
        }

        private void EnsureMapTextDefaults()
        {
            EnsureString(config.MapText.MapNamesRu, "normal", "Обычная карта сокровищ");
            EnsureString(config.MapText.MapNamesRu, "rare", "Редкая карта сокровищ");
            EnsureString(config.MapText.MapNamesRu, "epic", "Эпическая карта сокровищ");
            EnsureString(config.MapText.MapNamesRu, "legendary", "Легендарная карта сокровищ");

            EnsureString(config.MapText.MapNamesEn, "normal", "Common treasure map");
            EnsureString(config.MapText.MapNamesEn, "rare", "Rare treasure map");
            EnsureString(config.MapText.MapNamesEn, "epic", "Epic treasure map");
            EnsureString(config.MapText.MapNamesEn, "legendary", "Legendary treasure map");

            EnsureString(config.MapText.MapDescriptionsRu, "normal", "Как использовать?\nПоложите эту обычную карту сокровищ в исследовательский стол и осмотритесь вокруг.");
            EnsureString(config.MapText.MapDescriptionsRu, "rare", "Как использовать?\nПоложите эту редкую карту сокровищ в исследовательский стол и осмотритесь вокруг.");
            EnsureString(config.MapText.MapDescriptionsRu, "epic", "Как использовать?\nПоложите эту эпическую карту сокровищ в исследовательский стол и осмотритесь вокруг.");
            EnsureString(config.MapText.MapDescriptionsRu, "legendary", "Как использовать?\nПоложите эту легендарную карту сокровищ в исследовательский стол и осмотритесь вокруг.");

            EnsureString(config.MapText.MapDescriptionsEn, "normal", "How to use?\nPlace this common treasure map into a research table and look around.");
            EnsureString(config.MapText.MapDescriptionsEn, "rare", "How to use?\nPlace this rare treasure map into a research table and look around.");
            EnsureString(config.MapText.MapDescriptionsEn, "epic", "How to use?\nPlace this epic treasure map into a research table and look around.");
            EnsureString(config.MapText.MapDescriptionsEn, "legendary", "How to use?\nPlace this legendary treasure map into a research table and look around.");

            ReplaceIfEquals(config.MapText.MapDescriptionsRu, "normal", "Положите карту в исследовательский стол, чтобы найти обычный сундук с сокровищами.", "Как использовать?\nПоложите эту обычную карту сокровищ в исследовательский стол и осмотритесь вокруг.");
            ReplaceIfEquals(config.MapText.MapDescriptionsRu, "rare", "Положите карту в исследовательский стол, чтобы найти редкий сундук с сокровищами.", "Как использовать?\nПоложите эту редкую карту сокровищ в исследовательский стол и осмотритесь вокруг.");
            ReplaceIfEquals(config.MapText.MapDescriptionsRu, "epic", "Положите карту в исследовательский стол, чтобы найти эпический сундук с сокровищами.", "Как использовать?\nПоложите эту эпическую карту сокровищ в исследовательский стол и осмотритесь вокруг.");
            ReplaceIfEquals(config.MapText.MapDescriptionsRu, "legendary", "Положите карту в исследовательский стол, чтобы найти легендарный сундук с сокровищами.", "Как использовать?\nПоложите эту легендарную карту сокровищ в исследовательский стол и осмотритесь вокруг.");

            ReplaceIfEquals(config.MapText.MapDescriptionsEn, "normal", "Place this map into a research table to reveal a common treasure chest.", "How to use?\nPlace this common treasure map into a research table and look around.");
            ReplaceIfEquals(config.MapText.MapDescriptionsEn, "rare", "Place this map into a research table to reveal a rare treasure chest.", "How to use?\nPlace this rare treasure map into a research table and look around.");
            ReplaceIfEquals(config.MapText.MapDescriptionsEn, "epic", "Place this map into a research table to reveal an epic treasure chest.", "How to use?\nPlace this epic treasure map into a research table and look around.");
            ReplaceIfEquals(config.MapText.MapDescriptionsEn, "legendary", "Place this map into a research table to reveal a legendary treasure chest.", "How to use?\nPlace this legendary treasure map into a research table and look around.");

            ReplaceIfEquals(config.MapText.MapNamesRu, "normal", "ОБЫЧНАЯ КАРТА СОКРОВИЩ", "Обычная карта сокровищ");
            ReplaceIfEquals(config.MapText.MapNamesRu, "rare", "РЕДКАЯ КАРТА СОКРОВИЩ", "Редкая карта сокровищ");
            ReplaceIfEquals(config.MapText.MapNamesRu, "epic", "ЭПИЧЕСКАЯ КАРТА СОКРОВИЩ", "Эпическая карта сокровищ");
            ReplaceIfEquals(config.MapText.MapNamesRu, "legendary", "ЛЕГЕНДАРНАЯ КАРТА СОКРОВИЩ", "Легендарная карта сокровищ");

            ReplaceIfEquals(config.MapText.MapNamesEn, "normal", "COMMON TREASURE MAP", "Common treasure map");
            ReplaceIfEquals(config.MapText.MapNamesEn, "rare", "RARE TREASURE MAP", "Rare treasure map");
            ReplaceIfEquals(config.MapText.MapNamesEn, "epic", "EPIC TREASURE MAP", "Epic treasure map");
            ReplaceIfEquals(config.MapText.MapNamesEn, "legendary", "LEGENDARY TREASURE MAP", "Legendary treasure map");
        }

        private void EnsureMessageDefaults()
        {
            MessageSettings defaults = new MessageSettings();
            EnsureStrings(config.Messages.Ru, defaults.Ru);
            EnsureStrings(config.Messages.En, defaults.En);
        }

        private void EnsureStrings(Dictionary<string, string> target, Dictionary<string, string> defaults)
        {
            if (target == null || defaults == null) return;
            foreach (KeyValuePair<string, string> kvp in defaults)
                EnsureString(target, kvp.Key, kvp.Value);
        }

        private void EnsureString(Dictionary<string, string> dict, string key, string value)
        {
            if (dict == null || string.IsNullOrEmpty(key)) return;
            string current;
            if (!dict.TryGetValue(key, out current) || string.IsNullOrEmpty(current))
                dict[key] = value;
        }

        private void SetMapSkin(string key, ulong value)
        {
            if (config == null || config.MapText == null || config.MapText.MapSkinIds == null || string.IsNullOrEmpty(key)) return;
            config.MapText.MapSkinIds[key] = value;
        }

        private void FixLegacyMessageTexts()
        {
            ReplaceIfEquals(config.Messages.Ru, "MarkerText", "{0} сундук", "{0}");
            ReplaceIfEquals(config.Messages.En, "MarkerText", "{0} chest", "{0}");
            ReplaceIfEquals(config.Messages.Ru, "ChestCreated", "<color=white>Сокровища найдены! Ищите <color=#ffb300>{0}</color> сундук.</color>", "<color=white>Сокровища найдены! Ищите <color=#ffb300>{0}</color>.</color>");
            ReplaceIfEquals(config.Messages.En, "ChestCreated", "<color=white>Treasure revealed! Find the <color=#ffb300>{0}</color> chest.</color>", "<color=white>Treasure revealed! Find the <color=#ffb300>{0}</color>.</color>");
            ReplaceIfEquals(config.Messages.Ru, "ChestLooted", "<color=white>{0} сундук сокровищ был залутан и исчез.</color>", "<color=white>{0} был залутан и исчез.</color>");
            ReplaceIfEquals(config.Messages.En, "ChestLooted", "<color=white>The {0} treasure chest was looted and disappeared.</color>", "<color=white>The {0} was looted and disappeared.</color>");
        }

        private void ReplaceIfEquals(Dictionary<string, string> dict, string key, string oldValue, string newValue)
        {
            if (dict == null) return;
            string current;
            if (dict.TryGetValue(key, out current) && current == oldValue)
                dict[key] = newValue;
        }

        #endregion

        #region Data

        private enum TreasureType
        {
            Normal,
            Rare,
            Epic,
            Legendary
        }

        private class PlayerData
        {
            public readonly List<BaseEntity> Chests = new List<BaseEntity>();
        }

        private const string AdminUiName = "TreasureHunt.AdminSettings";
        private const string PlayerHudName = "TreasureHunt.PlayerHud";

        private readonly Dictionary<ulong, PlayerData> players = new Dictionary<ulong, PlayerData>();
        private readonly HashSet<NetworkableId> openedContainers = new HashSet<NetworkableId>();
        private readonly Dictionary<NetworkableId, TreasureType> chestTypes = new Dictionary<NetworkableId, TreasureType>();
        private readonly Dictionary<NetworkableId, ulong> chestOwners = new Dictionary<NetworkableId, ulong>();
        private readonly HashSet<ulong> hiddenHudUsers = new HashSet<ulong>();
        private readonly Dictionary<ulong, string> lastTreasureHudText = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, int> pendingTreasureSearches = new Dictionary<ulong, int>();
        private int cachedWaterTopologyMask = -1;

        private const string CustomMapIconBase64Normal = "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";
        private const string CustomMapIconBase64Rare = "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";
        private const string CustomMapIconBase64Epic = "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";
        private const string CustomMapIconBase64Legendary = "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";
        private readonly Dictionary<TreasureType, uint> customMapIconIds = new Dictionary<TreasureType, uint>();
        private bool customMapIconLoaded;


        private readonly string[] legacyRuMapNames =
        {
            "ОБЫЧНАЯ КАРТА СОКРОВИЩ",
            "РЕДКАЯ КАРТА СОКРОВИЩ",
            "ЭПИЧЕСКАЯ КАРТА СОКРОВИЩ",
            "ЛЕГЕНДАРНАЯ КАРТА СОКРОВИЩ"
        };

        private readonly TreasureType[] allTreasureTypes =
        {
            TreasureType.Normal,
            TreasureType.Rare,
            TreasureType.Epic,
            TreasureType.Legendary
        };

        #endregion

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(config.General.AdminPermission, this);

            cmd.AddChatCommand(config.General.MainCommand, this, "CmdTreasure");
            if (!string.Equals(config.General.MainCommand, config.General.ShortCommand, StringComparison.OrdinalIgnoreCase))
                cmd.AddChatCommand(config.General.ShortCommand, this, "CmdTreasure");

            cmd.AddChatCommand("getmap", this, "CmdGetMap");
            cmd.AddChatCommand("getraremap", this, "CmdGetRareMap");
            cmd.AddChatCommand("getepicmap", this, "CmdGetEpicMap");
            cmd.AddChatCommand("getlegendarymap", this, "CmdGetLegendaryMap");
            cmd.AddChatCommand("cleartreasure", this, "CmdClearTreasure");
            cmd.AddChatCommand("clearhud", this, "CmdClearHud");

            timer.Every(Mathf.Max(0.25f, config.Markers.CuiHudRefreshSeconds), RefreshAllTreasureHud);

            LoadCustomMapIcons();
        }

        private void OnServerInitialized()
        {
            LoadCustomMapIcons();
            TryOverrideMapDefinitionDescription();
            RegisterCustomMapItems();
        }

        private void OnCIDLoaded(Plugin library = null)
        {
            if (library != null) CustomItemDefinitionsPlugin = library;
            RegisterCustomMapItems();
        }

        private void Unload()
        {
            foreach (PlayerData pd in players.Values)
            {
                if (pd == null || pd.Chests == null) continue;
                for (int i = pd.Chests.Count - 1; i >= 0; i--)
                {
                    BaseEntity chest = pd.Chests[i];
                    if (chest != null && !chest.IsDestroyed)
                        chest.Kill();
                }
            }

            foreach (BasePlayer active in BasePlayer.activePlayerList)
            {
                DestroyAdminUi(active);
                DestroyTreasureHud(active);
                ClearMarkers(active);
            }

            players.Clear();
            chestTypes.Clear();
            chestOwners.Clear();
            openedContainers.Clear();
            hiddenHudUsers.Clear();
            lastTreasureHudText.Clear();
            pendingTreasureSearches.Clear();
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            DestroyAdminUi(player);
            DestroyTreasureHud(player);
            hiddenHudUsers.Remove(player.userID);
            pendingTreasureSearches.Remove(player.userID);
        }

        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (player == null || entity == null || !config.Drops.Enabled) return;

            StorageContainer container = entity as StorageContainer;
            if (container == null || container.net == null) return;

            if (config.Drops.OneRollPerCrate && openedContainers.Contains(container.net.ID))
                return;

            string shortName = container.ShortPrefabName ?? string.Empty;
            bool matched = false;

            for (int i = 0; i < config.Drops.Rules.Count; i++)
            {
                ContainerDropRule rule = config.Drops.Rules[i];
                if (rule == null || !rule.Enabled) continue;
                if (!MatchesContainerRule(shortName, rule)) continue;

                matched = true;
                TreasureType type;
                if (!TryParseType(rule.TreasureType, out type))
                    continue;

                if (Chance(rule.Chance))
                {
                    GiveMap(player, type);
                    DebugLog("Dropped " + TypeKey(type) + " map from " + shortName + " to " + player.displayName);
                }

                break;
            }

            if (config.Drops.OneRollPerCrate && matched)
                openedContainers.Add(container.net.ID);
        }

        private void OnLootEntityEnd(BasePlayer player, BaseEntity entity)
        {
            if (!config.Chest.RemoveWhenEmpty) return;

            StorageContainer container = entity as StorageContainer;
            if (container == null || container.inventory == null || container.net == null) return;
            if (!chestTypes.ContainsKey(container.net.ID)) return;
            if (container.inventory.itemList != null && container.inventory.itemList.Count > 0) return;

            ulong ownerId;
            if (!chestOwners.TryGetValue(container.net.ID, out ownerId)) return;

            TreasureType type = TreasureType.Normal;
            chestTypes.TryGetValue(container.net.ID, out type);

            DestroyTreasureChest(container, ownerId, false);

            if (player != null)
                Reply(player, "ChestLooted", GetChestName(type, player));
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            if (entity == null || entity.net == null) return;
            NetworkableId id = entity.net.ID;
            if (!chestTypes.ContainsKey(id)) return;

            ulong ownerId;
            if (chestOwners.TryGetValue(id, out ownerId))
                RemoveChestFromPlayer(ownerId, entity as BaseEntity);

            chestTypes.Remove(id);
            chestOwners.Remove(id);
        }

        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (container == null || item == null) return;

            ResearchTable table = container.entityOwner as ResearchTable;
            if (table == null) return;
            if (!IsTreasureMapItem(item)) return;

            TreasureType type;
            if (!TryGetMapType(item, out type)) return;

            BasePlayer activator = item.GetOwnerPlayer();
            if (activator == null)
                activator = FindLootingPlayer(table);

            if (activator == null || activator.IsDead()) return;
            if (Vector3.Distance(activator.transform.position, table.transform.position) > 10f) return;

            if (GetActiveChestCount(activator.userID) + GetPendingTreasureSearchCount(activator.userID) >= config.Chest.MaxActiveChestsPerPlayer)
            {
                Reply(activator, "TooManyChests", config.Chest.MaxActiveChestsPerPlayer);
                return;
            }

            item.RemoveFromContainer();
            item.Remove();
            StartTreasureSearch(activator, type, table.transform.position);
        }

        #endregion

        #region Map creation and parsing

        private void GiveMap(BasePlayer player, TreasureType type, bool sendFoundMessage = true)
        {
            if (player == null) return;

            string language = GetLanguage(player);
            Item map = CreateConfiguredMapItem(type, language);
            if (map == null) return;

            string typeKey = TypeKey(type);
            string mapName = GetMapName(type, language);
            string description = GetMapDescription(type, language);
            string technicalLine = "[TreasureHunt:" + typeKey + "]";

            map.name = mapName;
            map.text = BuildMapItemText(mapName, description, technicalLine);
            ApplyMapSkin(map, type);
            ApplyCustomMapIcon(map, type);
            // Для CID-предметов описание задаётся только при Register(). Не переписываем его здесь,
            // иначе клиент может снова увидеть token/пустой description.
            if (IsCustomMapDefinition(map.info))
            {
                ForceCustomMapDefinitionText(map.info, mapName, description);
            }
            else
            {
                TryOverrideMapItemDescription(map, description);
                ForceItemDefinitionText(map.info, mapName, description);
            }
            map.MarkDirty();

            player.GiveItem(map);
            if (sendFoundMessage)
                Reply(player, "MapFound", mapName);
        }

        private Item CreateConfiguredMapItem(TreasureType type, string language)
        {
            if (config.MapText.UseCustomItemDefinitions && customItemDefinitionsRegistered)
            {
                string customShortname = GetCustomMapShortname(type);
                Item customMap = ItemManager.CreateByName(customShortname, 1, GetMapSkinId(type));
                if (customMap != null)
                {
                    ApplyMapSkin(customMap, type);
                    return customMap;
                }
            }

            string itemShortname = GetConfiguredMapShortname();

            // запасной вариант, если CID не создал предмет
            Item map = ItemManager.CreateByName(itemShortname, 1, GetMapSkinId(type));
            if (map == null)
                map = ItemManager.CreateByName("documents", 1);
            if (map == null)
                map = ItemManager.CreateByName("scrap", 1);
            if (map == null)
                map = ItemManager.CreateByName("stones", 1);
            if (map == null)
                map = ItemManager.CreateByName("wood", 1);

            return map;
        }

        private bool IsCustomMapDefinition(ItemDefinition definition)
        {
            return definition != null && TryGetMapTypeFromCustomShortname(definition.shortname, out _);
        }

        private string GetCustomMapShortname(TreasureType type)
        {
            return GetCustomMapPrefix() + TypeKey(type);
        }

        private bool TryGetMapTypeFromCustomShortname(string shortname, out TreasureType type)
        {
            type = TreasureType.Normal;
            if (string.IsNullOrEmpty(shortname)) return false;

            if (TryGetMapTypeFromPrefix(shortname, GetCustomMapPrefix(), out type)) return true;
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.", out type)) return true; // старый префикс
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.v2.", out type)) return true; // старый префикс
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.v3.", out type)) return true;
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.v4.", out type)) return true;
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.v5.", out type)) return true;
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.v6.", out type)) return true;
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.v7.", out type)) return true;
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.v8.", out type)) return true;
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.v9.", out type)) return true;
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.v10.", out type)) return true;
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.v11.", out type)) return true;
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.v12.", out type)) return true;
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.v13.", out type)) return true;
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.v14.", out type)) return true;
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.v15.", out type)) return true;
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.v16.", out type)) return true;
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.v17.", out type)) return true;
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.v18.", out type)) return true;
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.v19.", out type)) return true;
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.v20.", out type)) return true;
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.v21.", out type)) return true;
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.v22.", out type)) return true;
            if (TryGetMapTypeFromPrefix(shortname, "treasurehunt.map.v23.", out type)) return true;

            return false;
        }

        private string GetCustomMapPrefix()
        {
            string prefix = "treasurehunt.map.v24.";
            if (config != null && config.MapText != null && !string.IsNullOrEmpty(config.MapText.CustomItemShortnamePrefix))
                prefix = config.MapText.CustomItemShortnamePrefix.Trim();

            if (!prefix.EndsWith(".", StringComparison.Ordinal))
                prefix += ".";

            return prefix;
        }

        private bool TryGetMapTypeFromPrefix(string shortname, string prefix, out TreasureType type)
        {
            type = TreasureType.Normal;
            if (string.IsNullOrEmpty(shortname) || string.IsNullOrEmpty(prefix)) return false;
            if (!prefix.EndsWith(".", StringComparison.Ordinal)) prefix += ".";

            for (int i = 0; i < allTreasureTypes.Length; i++)
            {
                TreasureType current = allTreasureTypes[i];
                if (!string.Equals(shortname, prefix + TypeKey(current), StringComparison.OrdinalIgnoreCase)) continue;
                type = current;
                return true;
            }

            return false;
        }

        private void RegisterCustomMapItems()
        {
            if (config == null || config.MapText == null || !config.MapText.UseCustomItemDefinitions) return;

            if (CustomItemDefinitionsPlugin == null)
            {
                WarnMissingCustomItemDefinitionsOnce();
                return;
            }

            if (CustomItemDefinitionsPlugin.Version == null || CustomItemDefinitionsPlugin.Version.Major < 2)
            {
                PrintWarning("CustomItemDefinitions слишком старый. Нужна версия 2.x или выше, иначе defaultDescription может не работать.");
                return;
            }

            if (customItemDefinitionsRegistered) return;

            ItemDefinition parentDefinition = FindCleanCustomMapParentDefinition();
            if (parentDefinition == null)
            {
                PrintWarning("Не найден подходящий ванильный предмет без описания. Кастомные карты не зарегистрированы.");
                return;
            }

            try
            {
                string cidLanguage = GetCustomItemDefinitionLanguage();

                for (int i = 0; i < allTreasureTypes.Length; i++)
                {
                    TreasureType type = allTreasureTypes[i];
                    string shortname = GetCustomMapShortname(type);
                    string name = GetMapName(type, cidLanguage);
                    string description = GetMapDescription(type, cidLanguage);

                    // defaultDescription даёт один блок описания без второго синего.
                    // Свои Translate.Phrase-токены не передаём, иначе клиент покажет сам токен
                    ItemDefinition registeredDefinition = CustomItemDefinitionsPlugin.Call<ItemDefinition>("Register", new
                    {
                        shortname = shortname,
                        parentItemId = parentDefinition.itemid,
                        maxStackSize = 1,
                        category = ItemCategory.Misc,
                        defaultName = name,
                        defaultDescription = MakeVisiblePhrase(description),
                        defaultSkinId = GetMapSkinId(type),
                        staticOwnerships = new List<(Translate.Phrase label, Translate.Phrase text)>(),
                        itemMods = new ItemMod[0],
                        repairable = false,
                        craftable = false
                    }, this);

                    if (registeredDefinition == null)
                        registeredDefinition = ItemManager.FindItemDefinition(shortname);

                    if (registeredDefinition == null)
                        return;

                    ForceCustomMapDefinitionText(registeredDefinition, name, description);
                }

                customItemDefinitionsRegistered = true;
                RefreshCustomMapDefinitionText();
                timer.Once(0.5f, RefreshCustomMapDefinitionText);
            }
            catch (Exception ex)
            {
                PrintError("Не удалось зарегистрировать кастомные предметы карт через CustomItemDefinitions: " + ex.Message);
            }
        }

        private void RefreshCustomMapDefinitionText()
        {
            string cidLanguage = GetCustomItemDefinitionLanguage();

            for (int i = 0; i < allTreasureTypes.Length; i++)
            {
                TreasureType type = allTreasureTypes[i];
                ItemDefinition definition = ItemManager.FindItemDefinition(GetCustomMapShortname(type));
                if (definition == null) continue;

                ForceCustomMapDefinitionText(definition, GetMapName(type, cidLanguage), GetMapDescription(type, cidLanguage));
            }
        }

        private ItemDefinition FindCleanCustomMapParentDefinition()
        {
            string configured = config != null && config.MapText != null ? config.MapText.CustomItemParentShortname : null;
            ItemDefinition configuredDefinition = FindCleanParentCandidate(configured);
            if (configuredDefinition != null)
                return configuredDefinition;

            string[] candidates =
            {
                "documents",
                "scrap",
                "stones",
                "wood",
                "metal.fragments",
                "cloth",
                "keycard_green",
                "keycard_blue",
                "keycard_red",
                "note",
                "paper",
                "fuse",
                "targeting.computer",
                "cctv.camera",
                "lowgradefuel",
                "sticks",
                "charcoal",
                "bone.fragments",
                "fat.animal",
                "sulfur"
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                ItemDefinition definition = FindCleanParentCandidate(candidates[i]);
                if (definition != null)
                    return definition;
            }

            return ItemManager.FindItemDefinition("documents");
        }

        private ItemDefinition FindCleanParentCandidate(string shortname)
        {
            if (string.IsNullOrEmpty(shortname)) return null;
            if (string.Equals(shortname.Trim(), "documents", StringComparison.OrdinalIgnoreCase))
                return ItemManager.FindItemDefinition("documents");

            ItemDefinition definition = ItemManager.FindItemDefinition(shortname.Trim());
            if (definition == null) return null;
            if (HasVisibleItemDescription(definition)) return null;

            return definition;
        }

        private void WarnMissingCustomItemDefinitionsOnce()
        {
            if (customItemDefinitionsMissingWarned) return;
            if (config == null || config.MapText == null) return;
            if (!config.MapText.WarnIfCustomItemDefinitionsMissing) return;
            if (IsCustomItemDefinitionsFileInstalled()) return;

            customItemDefinitionsMissingWarned = true;
            PrintWarning("CustomItemDefinitions не найден в папке plugins. Кастомные карты будут работать через запасной обычный предмет.");
        }

        private bool IsCustomItemDefinitionsFileInstalled()
        {
            try
            {
                string pluginDirectory = Interface.Oxide.PluginDirectory;
                if (string.IsNullOrEmpty(pluginDirectory) || !Directory.Exists(pluginDirectory))
                    return false;

                return File.Exists(Path.Combine(pluginDirectory, "CustomItemDefinitions.cs"))
                    || File.Exists(Path.Combine(pluginDirectory, "CustomItemDefinitions.dll"));
            }
            catch
            {
                return false;
            }
        }



        private ulong GetMapSkinId(TreasureType type)
        {
            string key = TypeKey(type);
            ulong skinId = 0UL;

            if (config != null && config.MapText != null && config.MapText.MapSkinIds != null)
                config.MapText.MapSkinIds.TryGetValue(key, out skinId);

            if (skinId == 0UL && config != null && config.MapText != null)
                skinId = config.MapText.MapSkinId;

            return skinId;
        }

        private void ApplyMapSkin(Item item, TreasureType type)
        {
            if (item == null) return;

            ulong skinId = GetMapSkinId(type);
            if (skinId == 0UL) return;

            item.skin = skinId;
            item.MarkDirty();
        }

        private void LoadCustomMapIcons()
        {
            if (customMapIconLoaded) return;
            if (CommunityEntity.ServerInstance == null || CommunityEntity.ServerInstance.net == null) return;

            customMapIconLoaded = true;

            customMapIconIds.Clear();
            LoadCustomMapIcon(TreasureType.Normal, CreateMapIconPng(TreasureType.Normal));
            LoadCustomMapIcon(TreasureType.Rare, CreateMapIconPng(TreasureType.Rare));
            LoadCustomMapIcon(TreasureType.Epic, CreateMapIconPng(TreasureType.Epic));
            LoadCustomMapIcon(TreasureType.Legendary, CreateMapIconPng(TreasureType.Legendary));
        }

        private void LoadCustomMapIcon(TreasureType type, string base64)
        {
            try
            {
                LoadCustomMapIcon(type, Convert.FromBase64String(base64));
            }
            catch (Exception ex)
            {
                PrintWarning("Не удалось загрузить кастомную иконку карты " + TypeKey(type) + ": " + ex.Message);
            }
        }

        private void LoadCustomMapIcon(TreasureType type, byte[] bytes)
        {
            try
            {
                if (bytes == null || bytes.Length == 0) return;

                uint imageId = FsStorePng(bytes);
                if (imageId == 0)
                {
                    PrintWarning("Не удалось загрузить кастомную иконку карты " + TypeKey(type) + " в FileStorage.");
                    return;
                }

                customMapIconIds[type] = imageId;
            }
            catch (Exception ex)
            {
                PrintWarning("Не удалось загрузить кастомную иконку карты " + TypeKey(type) + ": " + ex.Message);
            }
        }

        private byte[] CreateMapIconPng(TreasureType type)
        {
            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[size * size];

            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(0, 0, 0, 0);

            Color32 accent = GetMapIconAccent(type);
            float borderEdge = type == TreasureType.Legendary ? 0.84f : 0.90f;
            DrawMapIconShape(pixels, size, 68f, 58f, 72f, 84f, -12f, new Color32(0, 0, 0, 55), new Color32(0, 0, 0, 55), false);
            DrawMapIconShape(pixels, size, 64f, 64f, 72f, 84f, -12f, new Color32(188, 168, 124, 255), accent, true, borderEdge);

            texture.SetPixels32(pixels);
            texture.Apply();
            byte[] png = texture.EncodeToPNG();
            UnityEngine.Object.Destroy(texture);
            return png;
        }

        private Color32 GetMapIconAccent(TreasureType type)
        {
            switch (type)
            {
                case TreasureType.Rare:
                    return new Color32(38, 154, 220, 255);
                case TreasureType.Epic:
                    return new Color32(150, 78, 230, 255);
                case TreasureType.Legendary:
                    return new Color32(255, 215, 54, 255);
                default:
                    return new Color32(210, 210, 195, 255);
            }
        }

        private void DrawMapIconShape(Color32[] pixels, int size, float cx, float cy, float width, float height, float degrees, Color32 paper, Color32 accent, bool details, float borderEdge = 0.90f)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            float halfW = width * 0.5f;
            float halfH = height * 0.5f;
            Color32 ink = new Color32(84, 66, 43, 210);
            Color32 red = new Color32(155, 45, 34, 255);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    float lx = dx * cos + dy * sin;
                    float ly = -dx * sin + dy * cos;

                    if (Mathf.Abs(lx) > halfW || Mathf.Abs(ly) > halfH)
                        continue;

                    float edge = Mathf.Max(Mathf.Abs(lx) / halfW, Mathf.Abs(ly) / halfH);
                    Color32 color = edge > borderEdge ? accent : AddIconNoise(paper, x, y);

                    if (details)
                    {
                        if (Mathf.Abs(lx + 12f) < 1.1f || Mathf.Abs(ly - 9f) < 1.1f)
                            color = BlendIconColor(color, ink, 0.24f);

                        if (ly < -halfH + 11f && Mathf.Abs(lx) < halfW - 10f)
                            color = BlendIconColor(color, accent, 0.42f);

                        float route = Mathf.Sin(lx * 0.18f) * 5f - 4f;
                        if (Mathf.Abs(ly - route) < 1.4f && lx > -25f && lx < 20f)
                            color = BlendIconColor(color, ink, 0.55f);

                        if (Mathf.Abs((lx - 22f) - (ly + 17f)) < 1.8f && Mathf.Abs(lx - 22f) < 7f && Mathf.Abs(ly + 17f) < 7f)
                            color = red;

                        if (Mathf.Abs((lx - 22f) + (ly + 17f)) < 1.8f && Mathf.Abs(lx - 22f) < 7f && Mathf.Abs(ly + 17f) < 7f)
                            color = red;
                    }

                    pixels[y * size + x] = color;
                }
            }
        }

        private Color32 AddIconNoise(Color32 color, int x, int y)
        {
            int noise = ((x * 17 + y * 31) % 19) - 9;
            return new Color32(
                (byte)Mathf.Clamp(color.r + noise, 0, 255),
                (byte)Mathf.Clamp(color.g + noise, 0, 255),
                (byte)Mathf.Clamp(color.b + noise, 0, 255),
                color.a);
        }

        private Color32 BlendIconColor(Color32 a, Color32 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color32(
                (byte)Mathf.RoundToInt(a.r + (b.r - a.r) * t),
                (byte)Mathf.RoundToInt(a.g + (b.g - a.g) * t),
                (byte)Mathf.RoundToInt(a.b + (b.b - a.b) * t),
                (byte)Mathf.RoundToInt(a.a + (b.a - a.a) * t));
        }

        private void ApplyCustomMapIcon(Item item, TreasureType type)
        {
            if (item == null) return;
            if (!customMapIconLoaded)
                LoadCustomMapIcons();

            if (!customMapIconIds.TryGetValue(type, out uint imageId) || imageId == 0) return;

            try
            {
                if (TrySetItemImageId(item, "itemImageId", imageId)
                    || TrySetItemImageId(item, "imageId", imageId)
                    || TrySetItemImageId(item, "iconImageId", imageId))
                    item.MarkDirty();
            }
            catch
            {
                // На части чистых uMod-сборок itemImageId отсутствует. Тогда иконка останется от parent.
            }
        }

        private bool TrySetItemImageId(Item item, string memberName, uint imageId)
        {
            if (item == null || string.IsNullOrEmpty(memberName)) return false;

            Type itemType = typeof(Item);
            FieldInfo field = itemType.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                SetImageValue(field.FieldType, delegate(object value) { field.SetValue(item, value); }, imageId);
                return true;
            }

            PropertyInfo property = itemType.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite)
            {
                SetImageValue(property.PropertyType, delegate(object value) { property.SetValue(item, value, null); }, imageId);
                return true;
            }

            return false;
        }

        private void SetImageValue(Type valueType, Action<object> setter, uint imageId)
        {
            if (setter == null || valueType == null) return;

            if (valueType == typeof(uint))
                setter(imageId);
            else if (valueType == typeof(ulong))
                setter((ulong)imageId);
            else if (valueType == typeof(int))
                setter(unchecked((int)imageId));
            else
                setter(Convert.ChangeType(imageId, valueType));
        }

        private string GetCustomItemDefinitionLanguage()
        {
            if (config != null && config.General != null && string.Equals(config.General.Language, "en", StringComparison.OrdinalIgnoreCase))
                return "en";

            return "ru";
        }

        private Translate.Phrase MakeVisiblePhrase(string text)
        {
            if (string.IsNullOrEmpty(text))
                text = string.Empty;

            // текст и как токен, и как fallback, иначе местами виден токен
            return new Translate.Phrase(text, text);
        }

        private Translate.Phrase MakeEmptyPhrase()
        {
            return new Translate.Phrase(string.Empty, string.Empty);
        }

        private void ForceItemDefinitionText(ItemDefinition definition, string name, string description)
        {
            if (definition == null) return;

            try
            {
                definition.displayName = MakeVisiblePhrase(name);
                definition.displayDescription = MakeEmptyPhrase();
                ClearPhraseDeep(definition.displayDescription);
            }
            catch
            {
                TrySetPhraseEnglish(definition.displayName, name);
                ClearItemDefinitionDescription(definition);
            }
        }

        private void ForceCustomMapDefinitionText(ItemDefinition definition, string name, string description)
        {
            if (definition == null) return;
            if (string.IsNullOrEmpty(description))
                description = GetGenericInventoryDescription(GetCustomItemDefinitionLanguage());

            try
            {
                definition.displayName = MakeVisiblePhrase(name);
                definition.displayDescription = MakeVisiblePhrase(description);
            }
            catch
            {
                TrySetPhraseEnglish(definition.displayName, name);
                TrySetPhraseEnglish(definition.displayDescription, description);
            }
        }

        private void ClearItemDefinitionDescription(ItemDefinition definition)
        {
            if (definition == null) return;

            try
            {
                definition.displayDescription = MakeEmptyPhrase();
                ClearPhraseDeep(definition.displayDescription);
                return;
            }
            catch { }

            ClearPhraseDeep(definition.displayDescription);
        }

        private List<(Translate.Phrase label, Translate.Phrase text)> BuildMapTooltipBlocks(TreasureType type, string language)
        {
            if (config == null || config.MapText == null || !config.MapText.ShowCustomTooltipBlock)
                return null;

            string key = TypeKey(type);
            string label = language == "en" ? config.MapText.TooltipBlockLabelEn : config.MapText.TooltipBlockLabelRu;
            string description = GetMapDescription(type, language);

            if (string.IsNullOrEmpty(label)) label = language == "en" ? "HOW TO USE" : "КАК ИСПОЛЬЗОВАТЬ";
            if (string.IsNullOrEmpty(description)) description = GetGenericInventoryDescription(language);

            // обычные строки, CID сам сделает из них Phrase
            return new List<(Translate.Phrase label, Translate.Phrase text)>
            {
                (MakeVisiblePhrase(label), MakeVisiblePhrase(description))
            };
        }

        private string GetConfiguredMapShortname()
        {
            if (config == null || config.MapText == null || string.IsNullOrEmpty(config.MapText.MapItemShortname))
                return "documents";

            string shortname = config.MapText.MapItemShortname.Trim();
            if (string.Equals(shortname, "scrap", StringComparison.OrdinalIgnoreCase)
                || string.Equals(shortname, "researchpaper", StringComparison.OrdinalIgnoreCase)
                || string.Equals(shortname, "map", StringComparison.OrdinalIgnoreCase)
                || string.Equals(shortname, "rug", StringComparison.OrdinalIgnoreCase)
                || string.Equals(shortname, "rowboat", StringComparison.OrdinalIgnoreCase)
                || string.Equals(shortname, "rhib", StringComparison.OrdinalIgnoreCase)
                || string.Equals(shortname, "keycard_green", StringComparison.OrdinalIgnoreCase)
                || string.Equals(shortname, "keycard_blue", StringComparison.OrdinalIgnoreCase)
                || string.Equals(shortname, "spraycandecal", StringComparison.OrdinalIgnoreCase))
                return "documents";

            return shortname;
        }

        private string GetGenericInventoryDescription(string language)
        {
            if (config == null || config.MapText == null)
                return "How to use?\nPlace this treasure map into a research table and look around.";

            if (language == "en")
                return string.IsNullOrEmpty(config.MapText.InventoryDescriptionEn) ? "How to use?\nPlace this treasure map into a research table and look around." : config.MapText.InventoryDescriptionEn;

            return string.IsNullOrEmpty(config.MapText.InventoryDescriptionRu) ? "Как использовать?\nПоложите эту карту сокровищ в исследовательский стол и осмотритесь вокруг." : config.MapText.InventoryDescriptionRu;
        }

        private void TryOverrideMapDefinitionDescription()
        {
            if (config == null || config.MapText == null || !config.MapText.TryOverrideInventoryDescription) return;

            ItemDefinition definition = ItemManager.FindItemDefinition(GetConfiguredMapShortname());
            if (definition == null)
                definition = ItemManager.FindItemDefinition("map");
            if (definition == null) return;

            TrySetPhraseEnglish(definition.displayDescription, GetGenericInventoryDescription(config.General != null && config.General.Language == "en" ? "en" : "ru"));
        }

        private void TryOverrideMapItemDescription(Item item, string description)
        {
            if (item == null || item.info == null || config == null || config.MapText == null || !config.MapText.TryOverrideInventoryDescription) return;
            TrySetPhraseEnglish(item.info.displayDescription, description);
        }

        private void TrySetPhraseEnglish(object phrase, string text)
        {
            TrySetPhraseMember(phrase, "english", text);
        }

        private void ClearPhrase(object phrase)
        {
            TrySetPhraseMember(phrase, "token", string.Empty);
            TrySetPhraseMember(phrase, "english", string.Empty);
        }

        private void ClearPhraseDeep(object phrase)
        {
            if (phrase == null) return;

            ClearPhrase(phrase);

            Type type = phrase.GetType();
            string[] names =
            {
                "translated",
                "cached",
                "cachedPhrase",
                "englishPhrase",
                "fallback",
                "fallbackPhrase"
            };

            for (int i = 0; i < names.Length; i++)
                TrySetPhraseMember(phrase, names[i], string.Empty);

            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field == null || field.FieldType != typeof(string)) continue;
                field.SetValue(phrase, string.Empty);
            }

            PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo property = properties[i];
                if (property == null || property.PropertyType != typeof(string) || !property.CanWrite) continue;
                if (property.GetIndexParameters().Length > 0) continue;

                try
                {
                    property.SetValue(phrase, string.Empty, null);
                }
                catch { }
            }
        }

        private bool HasVisibleItemDescription(ItemDefinition definition)
        {
            if (definition == null || definition.displayDescription == null)
                return false;

            return HasVisiblePhraseText(definition.displayDescription);
        }

        private bool HasVisiblePhraseText(object phrase)
        {
            if (phrase == null) return false;

            Type type = phrase.GetType();

            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field == null || field.FieldType != typeof(string)) continue;

                string value = field.GetValue(phrase) as string;
                if (!string.IsNullOrWhiteSpace(value))
                    return true;
            }

            PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo property = properties[i];
                if (property == null || property.PropertyType != typeof(string) || !property.CanRead) continue;
                if (property.GetIndexParameters().Length > 0) continue;

                try
                {
                    string value = property.GetValue(phrase, null) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                        return true;
                }
                catch { }
            }

            return false;
        }

        private void TrySetPhraseMember(object phrase, string memberName, string text)
        {
            if (phrase == null || memberName == null || text == null) return;

            Type type = phrase.GetType();
            System.Reflection.FieldInfo field = type.GetField(memberName);
            if (field != null)
            {
                field.SetValue(phrase, text);
                return;
            }

            System.Reflection.PropertyInfo property = type.GetProperty(memberName);
            if (property != null && property.CanWrite)
                property.SetValue(phrase, text, null);
        }

        private string BuildMapItemText(string mapName, string description, string technicalLine)
        {
            if (config.MapText.ShowTechnicalIdLine)
                return technicalLine + "\n\n" + mapName + "\n\n" + description;

            return mapName + "\n\n" + description + "\n\n" + technicalLine;
        }

        private bool IsTreasureMapItem(Item item)
        {
            if (item == null || item.info == null) return false;

            string shortname = item.info.shortname ?? string.Empty;
            string configured = GetConfiguredMapShortname();
            TreasureType customType;

            if (TryGetMapTypeFromCustomShortname(shortname, out customType))
                return true;

            if (string.Equals(shortname, configured, StringComparison.OrdinalIgnoreCase))
                return true;

            if (config.MapText.AcceptLegacyNoteMaps && string.Equals(shortname, "note", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private bool TryGetMapType(Item item, out TreasureType type)
        {
            type = TreasureType.Normal;
            if (item == null) return false;

            if (item.info != null && TryGetMapTypeFromCustomShortname(item.info.shortname, out type))
                return true;

            string text = string.Empty;
            if (!string.IsNullOrEmpty(item.text)) text += item.text + "\n";
            if (!string.IsNullOrEmpty(item.name)) text += item.name + "\n";

            return TryGetMapType(text, out type);
        }

        private bool TryGetMapType(string text, out TreasureType type)
        {
            type = TreasureType.Normal;
            if (string.IsNullOrEmpty(text)) return false;

            string lower = text.ToLowerInvariant();
            if (lower.Contains("[treasurehunt:normal]")) { type = TreasureType.Normal; return true; }
            if (lower.Contains("[treasurehunt:rare]")) { type = TreasureType.Rare; return true; }
            if (lower.Contains("[treasurehunt:epic]")) { type = TreasureType.Epic; return true; }
            if (lower.Contains("[treasurehunt:legendary]")) { type = TreasureType.Legendary; return true; }

            for (int i = 0; i < legacyRuMapNames.Length; i++)
            {
                if (!text.Contains(legacyRuMapNames[i])) continue;
                type = (TreasureType)i;
                return true;
            }

            if (ContainsConfiguredMapName(text, config.MapText.MapNamesRu, TreasureType.Normal, out type)) return true;
            if (ContainsConfiguredMapName(text, config.MapText.MapNamesEn, TreasureType.Normal, out type)) return true;

            return false;
        }

        private bool ContainsConfiguredMapName(string text, Dictionary<string, string> names, TreasureType fallback, out TreasureType type)
        {
            type = fallback;
            if (names == null) return false;

            foreach (KeyValuePair<string, string> kvp in names)
            {
                if (string.IsNullOrEmpty(kvp.Value)) continue;
                if (!text.Contains(kvp.Value)) continue;

                if (!TryParseType(kvp.Key, out type))
                    type = fallback;
                return true;
            }

            return false;
        }

        #endregion

        #region Treasure generation

        private bool CreateTreasure(BasePlayer player, TreasureType type, Vector3 activationPosition)
        {
            if (player == null) return false;

            Vector3 spawnPos;
            if (!TryGetTreasurePosition(activationPosition, out spawnPos))
                return false;

            return CreateTreasureAtPosition(player, type, spawnPos);
        }

        private void StartTreasureSearch(BasePlayer player, TreasureType type, Vector3 activationPosition)
        {
            if (player == null) return;

            IncrementPendingTreasureSearch(player.userID);
            ContinueTreasureSearch(player.userID, type, activationPosition, 0);
        }

        private void ContinueTreasureSearch(ulong userId, TreasureType type, Vector3 activationPosition, int searchCycle)
        {
            if (GetPendingTreasureSearchCount(userId) <= 0)
                return;

            BasePlayer player = BasePlayer.FindByID(userId);
            if (player == null || !player.IsConnected)
            {
                DecrementPendingTreasureSearch(userId);
                return;
            }

            Vector3 spawnPos;
            if (TryGetTreasurePosition(activationPosition, out spawnPos, searchCycle))
            {
                if (CreateTreasureAtPosition(player, type, spawnPos))
                {
                    DecrementPendingTreasureSearch(userId);
                    return;
                }

                DecrementPendingTreasureSearch(userId);
                PrintWarning("Treasure chest prefab could not be created. Check Chest prefab in config.");
                return;
            }

            timer.Once(1f, delegate
            {
                ContinueTreasureSearch(userId, type, activationPosition, searchCycle + 1);
            });
        }

        private bool CreateTreasureAtPosition(BasePlayer player, TreasureType type, Vector3 spawnPos)
        {
            if (player == null) return false;

            BaseEntity chest = GameManager.server.CreateEntity(config.Chest.Prefab, spawnPos);
            if (chest == null) return false;

            chest.Spawn();

            StorageContainer storage = chest as StorageContainer;
            if (storage == null || storage.inventory == null)
            {
                chest.Kill();
                return false;
            }

            if (config.Chest.InventorySlots > 0)
                storage.inventory.capacity = config.Chest.InventorySlots;

            chest.name = GetChestName(type, player);
            AddLoot(storage, type);

            PlayerData playerData;
            if (!players.TryGetValue(player.userID, out playerData))
            {
                playerData = new PlayerData();
                players[player.userID] = playerData;
            }

            playerData.Chests.Add(chest);
            chestTypes[chest.net.ID] = type;
            chestOwners[chest.net.ID] = player.userID;
            hiddenHudUsers.Remove(player.userID);

            RefreshMarkers(player.userID);

            if (config.Chest.LifetimeMinutes > 0f)
            {
                timer.Once(config.Chest.LifetimeMinutes * 60f, delegate
                {
                    if (chest == null || chest.IsDestroyed || chest.net == null) return;
                    if (!chestTypes.ContainsKey(chest.net.ID)) return;

                    if (!config.Chest.RemoveAfterLifetime)
                    {
                        StorageContainer sc = chest as StorageContainer;
                        if (sc != null && sc.inventory != null && sc.inventory.itemList != null && sc.inventory.itemList.Count > 0)
                            return;
                    }

                    DestroyTreasureChest(chest, player.userID, false);
                    if (player != null && player.IsConnected)
                        Reply(player, "ChestExpired");
                });
            }

            Reply(player, "ChestCreated", GetChestName(type, player));
            return true;
        }

        private bool TryGetTreasurePosition(Vector3 start, out Vector3 result)
        {
            return TryGetTreasurePosition(start, out result, 0);
        }

        private bool TryGetTreasurePosition(Vector3 start, out Vector3 result, int searchCycle)
        {
            int attempts = Mathf.Max(1, config.Spawn.Attempts);
            float minDistance = Mathf.Max(0f, config.Spawn.MinTreasureDistance);
            float baseMaxDistance = Mathf.Max(minDistance + 1f, config.Spawn.MaxTreasureDistance);
            float expansion = 1f + Mathf.Min(4f, Mathf.Max(0, searchCycle) * 0.15f);
            float maxDistance = baseMaxDistance * expansion;

            if (searchCycle >= 6)
                minDistance = Mathf.Max(0f, minDistance * 0.5f);

            for (int i = 0; i < attempts; i++)
            {
                float dist = UnityEngine.Random.Range(minDistance, maxDistance);
                float angle = UnityEngine.Random.Range(0f, 360f);
                Vector3 candidate = start + (Quaternion.Euler(0f, angle, 0f) * Vector3.forward * dist);

                Vector3 ground;
                if (!TryGetGroundPosition(candidate, out ground))
                    continue;

                ground.y += config.Spawn.GroundLift;
                if (!IsPositionValid(ground))
                    continue;

                result = ground;
                return true;
            }

            result = Vector3.zero;
            return false;
        }

        private bool TryGetGroundPosition(Vector3 pos, out Vector3 groundPos)
        {
            groundPos = Vector3.zero;

            RaycastHit hit;
            if (!TryRaycastSafeGround(pos, out hit))
                return false;

            if (!IsGroundAreaFlat(hit.point))
                return false;

            groundPos = hit.point;
            return true;
        }

        private bool TryRaycastSafeGround(Vector3 pos, out RaycastHit hit)
        {
            hit = new RaycastHit();

            if (TerrainMeta.HeightMap == null)
                return false;

            Vector3 origin = new Vector3(pos.x, TerrainMeta.HeightMap.GetHeight(pos) + 300f, pos.z);
            int mask = LayerMask.GetMask("Terrain", "World", "Construction", "Deployed");
            if (mask == 0)
                mask = LayerMask.GetMask("Terrain");

            if (!Physics.Raycast(origin, Vector3.down, out hit, 600f, mask, QueryTriggerInteraction.Ignore))
                return false;

            if (config.Spawn.TerrainOnly || config.Spawn.AvoidObstacles)
            {
                if (!IsTerrainHit(hit))
                    return false;
            }
            else
            {
                BaseEntity hitEntity = hit.GetEntity();
                if (hitEntity != null)
                    return false;
            }

            if (Vector3.Angle(hit.normal, Vector3.up) > Mathf.Clamp(config.Spawn.MaxGroundSlope, 1f, 89f))
                return false;

            return true;
        }

        private bool IsTerrainHit(RaycastHit hit)
        {
            return hit.collider != null
                && hit.collider.gameObject != null
                && hit.collider.gameObject.layer == LayerMask.NameToLayer("Terrain");
        }

        private bool IsGroundAreaFlat(Vector3 center)
        {
            float radius = Mathf.Max(0f, config.Spawn.FlatCheckRadius);
            if (radius <= 0f)
                return true;

            float maxHeightDifference = Mathf.Max(0.1f, config.Spawn.MaxGroundHeightDifference);
            int samples = 8;

            for (int i = 0; i < samples; i++)
            {
                float angle = (360f / samples) * i;
                Vector3 sample = center + (Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius);

                RaycastHit sampleHit;
                if (!TryRaycastSafeGround(sample, out sampleHit))
                    return false;

                if (Mathf.Abs(sampleHit.point.y - center.y) > maxHeightDifference)
                    return false;
            }

            return true;
        }

        private bool PassesWaterCheck(Vector3 pos)
        {
            if (TerrainMeta.WaterMap == null && TerrainMeta.TopologyMap == null)
                return true;

            float padding = Mathf.Max(0f, config.Spawn.WaterHeightPadding);

            if (IsWaterAtSample(pos, padding))
                return false;

            if (config.Spawn.RejectWaterTopology && HasWaterTopology(pos))
                return false;

            float minDistance = Mathf.Max(0f, config.Spawn.MinDistanceFromWater);
            if (minDistance <= 0f)
                return true;

            float step = Mathf.Clamp(config.Spawn.WaterCheckStep, 1f, minDistance);
            int angleSamples = 24;

            for (float radius = step; radius <= minDistance + 0.01f; radius += step)
            {
                for (int i = 0; i < angleSamples; i++)
                {
                    float angle = (360f / angleSamples) * i;
                    Vector3 sample = pos + (Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius);
                    if (IsWaterAtSample(sample, padding))
                        return false;

                    if (config.Spawn.RejectWaterTopology && HasWaterTopology(sample))
                        return false;
                }
            }

            return true;
        }

        private bool IsWaterAtSample(Vector3 sample, float padding)
        {
            if (TerrainMeta.WaterMap == null)
                return false;

            float water = TerrainMeta.WaterMap.GetHeight(sample);
            float ground = sample.y;
            if (TerrainMeta.HeightMap != null)
                ground = TerrainMeta.HeightMap.GetHeight(sample);

            return ground <= water + padding;
        }

        private bool HasWaterTopology(Vector3 pos)
        {
            if (TerrainMeta.TopologyMap == null)
                return false;

            if (cachedWaterTopologyMask < 0)
                cachedWaterTopologyMask = BuildWaterTopologyMask();

            if (cachedWaterTopologyMask == 0)
                return false;

            int topology = TerrainMeta.TopologyMap.GetTopology(pos);
            return (topology & cachedWaterTopologyMask) != 0;
        }

        private int BuildWaterTopologyMask()
        {
            int mask = 0;
            string[] names = { "Ocean", "Oceanside", "Offshore", "Lake", "Lakeside", "River", "Riverside", "Swamp" };

            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    object value = Enum.Parse(typeof(TerrainTopology.Enum), names[i], true);
                    mask |= (int)value;
                }
                catch
                {
                    // не во всех версиях Rust есть все значения топологии
                }
            }

            return mask;
        }

        private bool IsPositionValid(Vector3 pos)
        {
            if (config.Spawn.AvoidWater && !PassesWaterCheck(pos))
                return false;

            if (config.Spawn.MinMonumentDistance > 0f && TerrainMeta.Path != null && TerrainMeta.Path.Monuments != null)
            {
                foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
                {
                    if (monument == null) continue;
                    if (Vector3.Distance(pos, monument.transform.position) < config.Spawn.MinMonumentDistance)
                        return false;
                }
            }

            if (!config.Spawn.AvoidObstacles)
                return true;

            float radius = Mathf.Max(0.5f, config.Spawn.ObstacleCheckRadius);
            List<BaseEntity> entities = new List<BaseEntity>();

            try
            {
                Vis.Entities(pos, radius, entities, LayerMask.GetMask("Construction", "Deployed"), QueryTriggerInteraction.Ignore);
                if (entities.Count > 0)
                    return false;

                entities.Clear();
                Vis.Entities(pos, radius, entities, LayerMask.GetMask("World"), QueryTriggerInteraction.Ignore);
                for (int i = 0; i < entities.Count; i++)
                {
                    BaseEntity e = entities[i];
                    if (e == null) continue;
                    if (e is TreeEntity) return false;

                    string prefabName = (e.ShortPrefabName ?? string.Empty).ToLowerInvariant();
                    if (prefabName.Contains("rock") || prefabName.Contains("tree") || prefabName.Contains("boulder") || prefabName.Contains("cliff"))
                        return false;
                }
            }
            finally
            {
                entities.Clear();
            }

            int overlapMask = LayerMask.GetMask("Construction", "Deployed", "World");
            Collider[] hits = Physics.OverlapSphere(pos, radius, overlapMask, QueryTriggerInteraction.Ignore);
            if (hits != null && hits.Length > 0)
            {
                for (int i = 0; i < hits.Length; i++)
                {
                    Collider hit = hits[i];
                    if (hit == null) continue;

                    BaseEntity entity = hit.ToBaseEntity();
                    if (entity != null)
                    {
                        if (entity is TreeEntity) return false;
                        if (entity is BuildingBlock || entity is DecayEntity) return false;
                    }

                    string objectName = hit.gameObject == null ? string.Empty : hit.gameObject.name.ToLowerInvariant();
                    if (objectName.Contains("rock") || objectName.Contains("tree") || objectName.Contains("building") || objectName.Contains("monument"))
                        return false;
                }
            }

            if (config.Spawn.MinBuildingDistance > 0f)
            {
                List<BuildingBlock> blocks = new List<BuildingBlock>();
                try
                {
                    Vis.Entities(pos, config.Spawn.MinBuildingDistance, blocks, LayerMask.GetMask("Construction"), QueryTriggerInteraction.Ignore);
                    if (blocks.Count > 0)
                        return false;
                }
                finally
                {
                    blocks.Clear();
                }
            }

            return true;
        }

        private void DestroyTreasureChest(BaseEntity chest, ulong ownerId, bool refreshOnly)
        {
            if (chest == null) return;

            if (chest.net != null)
            {
                chestTypes.Remove(chest.net.ID);
                chestOwners.Remove(chest.net.ID);
            }

            RemoveChestFromPlayer(ownerId, chest);

            if (!refreshOnly && !chest.IsDestroyed)
                chest.Kill();

            RefreshMarkers(ownerId);
        }

        private void RemoveChestFromPlayer(ulong ownerId, BaseEntity chest)
        {
            PlayerData pd;
            if (!players.TryGetValue(ownerId, out pd) || pd == null || pd.Chests == null)
                return;

            for (int i = pd.Chests.Count - 1; i >= 0; i--)
            {
                BaseEntity stored = pd.Chests[i];
                if (stored == null || stored.IsDestroyed || stored == chest)
                    pd.Chests.RemoveAt(i);
            }

            if (pd.Chests.Count == 0)
                players.Remove(ownerId);
        }

        private int GetActiveChestCount(ulong userId)
        {
            PlayerData pd;
            if (!players.TryGetValue(userId, out pd) || pd == null || pd.Chests == null)
                return 0;

            int count = 0;
            for (int i = pd.Chests.Count - 1; i >= 0; i--)
            {
                BaseEntity chest = pd.Chests[i];
                if (chest == null || chest.IsDestroyed)
                {
                    pd.Chests.RemoveAt(i);
                    continue;
                }
                count++;
            }

            return count;
        }

        private int GetPendingTreasureSearchCount(ulong userId)
        {
            int count;
            return pendingTreasureSearches.TryGetValue(userId, out count) ? Mathf.Max(0, count) : 0;
        }

        private void IncrementPendingTreasureSearch(ulong userId)
        {
            pendingTreasureSearches[userId] = GetPendingTreasureSearchCount(userId) + 1;
        }

        private void DecrementPendingTreasureSearch(ulong userId)
        {
            int count = GetPendingTreasureSearchCount(userId) - 1;
            if (count <= 0)
                pendingTreasureSearches.Remove(userId);
            else
                pendingTreasureSearches[userId] = count;
        }

        #endregion

        #region Loot

        private void AddLoot(StorageContainer container, TreasureType type)
        {
            if (container == null || container.inventory == null) return;

            LootProfile profile = GetLootProfile(type);
            if (profile == null || profile.Items == null) return;

            int max = Mathf.Max(0, profile.MaxItems);
            int min = Mathf.Clamp(profile.MinItems, 0, max);
            int target = UnityEngine.Random.Range(min, max + 1);
            int added = 0;

            List<LootItem> items = new List<LootItem>();
            for (int itemIndex = 0; itemIndex < profile.Items.Count; itemIndex++)
            {
                LootItem candidate = profile.Items[itemIndex];
                if (candidate == null || IsBlockedLootShortname(candidate.Shortname)) continue;
                items.Add(candidate);
            }
            Shuffle(items);

            for (int i = 0; i < items.Count && added < target; i++)
            {
                LootItem lootItem = items[i];
                if (lootItem == null) continue;
                if (!Chance(lootItem.Chance)) continue;
                if (CreateAndMoveItem(container, lootItem))
                    added++;
            }

            int safety = 0;
            while (added < min && items.Count > 0 && safety < 50)
            {
                safety++;
                LootItem lootItem = items[UnityEngine.Random.Range(0, items.Count)];
                if (CreateAndMoveItem(container, lootItem))
                    added++;
            }

            if (profile.RollWeaponSeparately && profile.Weapons != null && profile.Weapons.Count > 0 && added < max && Chance(profile.WeaponRollChance))
            {
                LootItem weapon = GetRandomLootItem(profile.Weapons);
                if (weapon != null && CreateAndMoveItem(container, weapon))
                    added++;
            }
        }

        private bool CreateAndMoveItem(StorageContainer container, LootItem lootItem)
        {
            if (container == null || container.inventory == null || lootItem == null || string.IsNullOrEmpty(lootItem.Shortname))
                return false;
            if (IsBlockedLootShortname(lootItem.Shortname))
                return false;

            int min = Mathf.Max(1, lootItem.Min);
            int max = Mathf.Max(min, lootItem.Max);
            int amount = UnityEngine.Random.Range(min, max + 1);

            Item item = ItemManager.CreateByName(lootItem.Shortname, amount);
            if (item == null)
            {
                DebugLog("Invalid item shortname in loot table: " + lootItem.Shortname);
                return false;
            }

            if (!item.MoveToContainer(container.inventory))
            {
                item.Remove();
                return false;
            }

            return true;
        }

        private LootItem GetRandomLootItem(List<LootItem> list)
        {
            if (list == null || list.Count == 0) return null;

            List<LootItem> valid = new List<LootItem>();
            for (int i = 0; i < list.Count; i++)
            {
                LootItem item = list[i];
                if (item == null || IsBlockedLootShortname(item.Shortname)) continue;
                if (Chance(item.Chance)) valid.Add(item);
            }

            if (valid.Count == 0)
                return null;

            return valid[UnityEngine.Random.Range(0, valid.Count)];
        }

        private LootProfile GetLootProfile(TreasureType type)
        {
            switch (type)
            {
                case TreasureType.Rare:
                    return config.Loot.Rare;
                case TreasureType.Epic:
                    return config.Loot.Epic;
                case TreasureType.Legendary:
                    return config.Loot.Legendary;
                default:
                    return config.Loot.Normal;
            }
        }

        #endregion

        #region Markers

        private void RefreshMarkers(ulong userId, bool clearExisting = true)
        {
            if (!config.Markers.Enabled) return;

            BasePlayer player = BasePlayer.FindByID(userId);
            if (player == null || !player.IsConnected) return;

            bool drawDdrawMarkers = config.Markers.DdrawTextEnabled || config.Markers.DdrawBoxEnabled;

            if (clearExisting && drawDdrawMarkers && config.Markers.ClearBeforeRedraw)
                ClearMarkers(player);

            PlayerData pd;
            if (!players.TryGetValue(userId, out pd) || pd == null || pd.Chests == null)
                return;

            if (drawDdrawMarkers)
            {
                for (int i = 0; i < pd.Chests.Count; i++)
                {
                    BaseEntity chest = pd.Chests[i];
                    if (chest == null || chest.IsDestroyed || chest.net == null) continue;

                    TreasureType type = TreasureType.Normal;
                    chestTypes.TryGetValue(chest.net.ID, out type);
                    DrawMarker(player, chest.transform.position, type);
                }
            }

            RefreshTreasureHud(player);
        }

        private void ClearMarkers(BasePlayer player)
        {
            if (player == null) return;
            player.SendConsoleCommand("ddraw.clearall");
            player.SendConsoleCommand("ddraw.clear");
            player.SendConsoleCommand("ddraw.removeall");
            player.SendConsoleCommand("ddraw.removetextall");
        }

        private void DrawMarker(BasePlayer player, Vector3 position, TreasureType type)
        {
            if (player == null || !config.Markers.Enabled) return;

            string typeKey = TypeKey(type);
            string markerText = string.Format(GetMessage("MarkerText", player), GetChestName(type, player));
            float playerDistance = Vector3.Distance(player.transform.position, position);
            string distanceText = Mathf.RoundToInt(playerDistance).ToString(CultureInfo.InvariantCulture) + "\u043c";
            Color color = GetMarkerColor(typeKey);

            if (config.Markers.DdrawTextEnabled)
            {
                int size = Mathf.RoundToInt(Mathf.Clamp(config.Markers.DdrawTextSize, 6f, 40f));
                Vector3 textPosition = position + Vector3.up * config.Markers.TextHeight;
                string text = "<size=" + size.ToString(CultureInfo.InvariantCulture) + "><b>" + markerText + "\n" + distanceText + "</b></size>";
                DrawDdrawText(player, textPosition, color, text);
            }

            if (config.Markers.DdrawBoxEnabled)
                player.SendConsoleCommand("ddraw.box", GetDdrawDrawDuration(), color, position, config.Markers.BoxSize);
        }

        private float GetDdrawDrawDuration()
        {
            if (config.Markers.DdrawTextEnabled)
                return Mathf.Clamp(config.Markers.CuiHudRefreshSeconds + Time.deltaTime, 0.1f, 1.1f);

            return config.Markers.DurationSeconds;
        }

        private void DrawDdrawText(BasePlayer player, Vector3 position, Color color, string text)
        {
            player.SendConsoleCommand("ddraw.text", GetDdrawDrawDuration(), color, position, text);
        }

        private Color GetMarkerColor(string typeKey)
        {
            ColorConfig cc;
            if (config.Markers.Colors != null && config.Markers.Colors.TryGetValue(typeKey, out cc) && cc != null)
                return new Color(Mathf.Clamp01(cc.R), Mathf.Clamp01(cc.G), Mathf.Clamp01(cc.B), Mathf.Clamp01(cc.A));

            return Color.white;
        }

        #endregion

        #region CUI

        private void RefreshAllTreasureHud()
        {
            if (BasePlayer.activePlayerList == null) return;

            bool redrawDdraw = config.Markers != null
                && config.Markers.Enabled
                && config.Markers.DdrawTextEnabled;

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (redrawDdraw)
                    RefreshMarkers(player.userID, false);

                RefreshTreasureHud(player);
            }
        }

        private void RefreshTreasureHud(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;

            if (!config.Markers.Enabled || !config.Markers.CuiHudEnabled || hiddenHudUsers.Contains(player.userID))
            {
                DestroyTreasureHud(player);
                return;
            }

            PlayerData pd;
            if (!players.TryGetValue(player.userID, out pd) || pd == null || pd.Chests == null || pd.Chests.Count == 0)
            {
                DestroyTreasureHud(player);
                return;
            }

            List<string> lines = new List<string>();
            int maxChests = Mathf.Max(1, config.Markers.CuiHudMaxLines);
            int shownChests = 0;

            for (int i = pd.Chests.Count - 1; i >= 0; i--)
            {
                BaseEntity chest = pd.Chests[i];
                if (chest == null || chest.IsDestroyed || chest.net == null)
                {
                    pd.Chests.RemoveAt(i);
                    continue;
                }

                TreasureType type = TreasureType.Normal;
                chestTypes.TryGetValue(chest.net.ID, out type);

                float distance = Vector3.Distance(player.transform.position, chest.transform.position);
                lines.Add(GetChestName(type, player));
                lines.Add(Mathf.RoundToInt(distance).ToString(CultureInfo.InvariantCulture) + "\u043c");
                shownChests++;

                if (shownChests >= maxChests)
                    break;
            }

            if (lines.Count == 0)
            {
                DestroyTreasureHud(player);
                return;
            }

            string text = string.Join("\n", lines.ToArray());
            string previousText;
            if (lastTreasureHudText.TryGetValue(player.userID, out previousText) && previousText == text)
                return;

            DestroyTreasureHud(player);

            CuiElementContainer container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                Image = { Color = config.Markers.CuiHudPanelColor },
                RectTransform = { AnchorMin = config.Markers.CuiHudAnchorMin, AnchorMax = config.Markers.CuiHudAnchorMax },
                CursorEnabled = false
            }, "Overlay", PlayerHudName);

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = text,
                    FontSize = config.Markers.CuiHudFontSize,
                    Align = TextAnchor.MiddleCenter,
                    Color = config.Markers.CuiHudTextColor
                },
                RectTransform = { AnchorMin = "0.02 0.02", AnchorMax = "0.98 0.98" }
            }, PlayerHudName);

            CuiHelper.AddUi(player, container);
            lastTreasureHudText[player.userID] = text;
        }

        private void DestroyTreasureHud(BasePlayer player)
        {
            if (player == null) return;
            lastTreasureHudText.Remove(player.userID);
            CuiHelper.DestroyUi(player, PlayerHudName);
        }

        private void HideTreasureHud(BasePlayer player)
        {
            if (player == null) return;
            hiddenHudUsers.Add(player.userID);
            DestroyTreasureHud(player);
        }

        private void OpenAdminUi(BasePlayer player)
        {
            if (player == null) return;

            DestroyAdminUi(player);

            CuiElementContainer container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                Image = { Color = "0.05 0.05 0.05 0.94" },
                RectTransform = { AnchorMin = "0.12 0.06", AnchorMax = "0.88 0.94" },
                CursorEnabled = true
            }, "Overlay", AdminUiName);

            AddUiLabel(container, AdminUiName, "Treasure Hunt — CUI настройки", 20, "0.04 0.91", "0.96 0.98", TextAnchor.MiddleLeft, "1 0.82 0.25 1");
            AddUiLabel(container, AdminUiName, "ddraw.text рисуется как в AdminESP: размер задаётся через <size>, а обновление идёт короткими кадрами без шлейфа.", 11, "0.04 0.84", "0.96 0.90", TextAnchor.MiddleLeft, "0.86 0.86 0.86 1");

            AddUiLabel(container, AdminUiName, "Названия сундуков:", 13, "0.04 0.78", "0.30 0.83", TextAnchor.MiddleLeft, "1 1 1 1");
            AddUiLabel(container, AdminUiName, GetChestName(TreasureType.Normal, player) + " / " + GetChestName(TreasureType.Rare, player) + " / " + GetChestName(TreasureType.Epic, player) + " / " + GetChestName(TreasureType.Legendary, player), 12, "0.30 0.78", "0.96 0.83", TextAnchor.MiddleLeft, "0.86 0.86 0.86 1");
            AddUiLabel(container, AdminUiName, "Сменить название: /th name ru normal Обычный сундук", 11, "0.04 0.73", "0.96 0.77", TextAnchor.MiddleLeft, "0.70 0.70 0.70 1");

            float y = 0.70f;
            AddUiToggleRow(container, ref y, "Язык сообщений", config.General.Language, "treasurehunt.ui lang_ru", "RU", "treasurehunt.ui lang_en", "EN", "treasurehunt.ui lang_auto", "AUTO");
            AddUiToggleRow(container, ref y, "Маркеры", OnOff(config.Markers.Enabled), "treasurehunt.ui toggle_markers", "ON/OFF", null, null, null, null);
            AddUiToggleRow(container, ref y, "ddraw текст над сундуком", OnOff(config.Markers.DdrawTextEnabled), "treasurehunt.ui toggle_ddraw_text", "ON/OFF", null, null, null, null);
            AddUiToggleRow(container, ref y, "ddraw box", OnOff(config.Markers.DdrawBoxEnabled), "treasurehunt.ui toggle_ddraw_box", "ON/OFF", null, null, null, null);
            AddUiToggleRow(container, ref y, "CUI HUD", OnOff(config.Markers.CuiHudEnabled), "treasurehunt.ui toggle_cui_hud", "ON/OFF", null, null, null, null);
            AddUiToggleRow(container, ref y, "Запрет спавна в воде", OnOff(config.Spawn.AvoidWater), "treasurehunt.ui toggle_avoid_water", "ON/OFF", null, null, null, null);
            AddUiToggleRow(container, ref y, "Только terrain, без морских скал", OnOff(config.Spawn.TerrainOnly), "treasurehunt.ui toggle_terrain_only", "ON/OFF", null, null, null, null);
            AddUiToggleRow(container, ref y, "Проверка Ocean/Lake/River topology", OnOff(config.Spawn.RejectWaterTopology), "treasurehunt.ui toggle_water_topology", "ON/OFF", null, null, null, null);
            AddUiNumberRow(container, ref y, "Мин. от воды", config.Spawn.MinDistanceFromWater.ToString("0", CultureInfo.InvariantCulture), "treasurehunt.ui waterdist_minus", "treasurehunt.ui waterdist_plus");
            AddUiNumberRow(container, ref y, "Высота ddraw текста", config.Markers.TextHeight.ToString("0.0", CultureInfo.InvariantCulture), "treasurehunt.ui height_minus", "treasurehunt.ui height_plus");
            AddUiNumberRow(container, ref y, "Размер ddraw текста", Mathf.RoundToInt(config.Markers.DdrawTextSize).ToString(CultureInfo.InvariantCulture), "treasurehunt.ui textsize_minus", "treasurehunt.ui textsize_plus");
            AddUiNumberRow(container, ref y, "Размер ddraw box", config.Markers.BoxSize.ToString("0.0", CultureInfo.InvariantCulture), "treasurehunt.ui box_minus", "treasurehunt.ui box_plus");
            AddUiNumberRow(container, ref y, "Жизнь сундука, мин", config.Chest.LifetimeMinutes.ToString("0", CultureInfo.InvariantCulture), "treasurehunt.ui lifetime_minus", "treasurehunt.ui lifetime_plus");
            AddUiNumberRow(container, ref y, "Макс. сундуков на игрока", config.Chest.MaxActiveChestsPerPlayer.ToString(CultureInfo.InvariantCulture), "treasurehunt.ui maxchests_minus", "treasurehunt.ui maxchests_plus");
            AddUiNumberRow(container, ref y, "Мин. дистанция", config.Spawn.MinTreasureDistance.ToString("0", CultureInfo.InvariantCulture), "treasurehunt.ui mindist_minus", "treasurehunt.ui mindist_plus");
            AddUiNumberRow(container, ref y, "Макс. дистанция", config.Spawn.MaxTreasureDistance.ToString("0", CultureInfo.InvariantCulture), "treasurehunt.ui maxdist_minus", "treasurehunt.ui maxdist_plus");

            AddUiButton(container, AdminUiName, "Обновить", "treasurehunt.ui open", "0.04 0.03", "0.24 0.08", "0.18 0.35 0.18 1");
            AddUiButton(container, AdminUiName, "Сохранено автоматически", "treasurehunt.ui open", "0.28 0.03", "0.58 0.08", "0.12 0.12 0.12 1");
            AddUiButton(container, AdminUiName, "Закрыть", "treasurehunt.ui close", "0.76 0.03", "0.96 0.08", "0.60 0.12 0.12 1");

            CuiHelper.AddUi(player, container);
        }

        private string OnOff(bool value)
        {
            return value ? "ВКЛ" : "ВЫКЛ";
        }

        private void DestroyAdminUi(BasePlayer player)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, AdminUiName);
        }

        private void AddUiToggleRow(CuiElementContainer container, ref float y, string label, string value, string command1, string text1, string command2, string text2, string command3, string text3)
        {
            AddUiLabel(container, AdminUiName, label, 11, Anchor(0.04f, y, 0.46f, y + 0.034f), TextAnchor.MiddleLeft, "1 1 1 1");
            AddUiLabel(container, AdminUiName, value, 11, Anchor(0.48f, y, 0.57f, y + 0.034f), TextAnchor.MiddleCenter, "1 0.82 0.25 1");
            AddUiButton(container, AdminUiName, text1, command1, Anchor(0.60f, y, 0.72f, y + 0.034f), "0.17 0.17 0.17 1");
            if (!string.IsNullOrEmpty(command2)) AddUiButton(container, AdminUiName, text2, command2, Anchor(0.74f, y, 0.84f, y + 0.034f), "0.17 0.17 0.17 1");
            if (!string.IsNullOrEmpty(command3)) AddUiButton(container, AdminUiName, text3, command3, Anchor(0.86f, y, 0.96f, y + 0.034f), "0.17 0.17 0.17 1");
            y -= 0.041f;
        }

        private void AddUiNumberRow(CuiElementContainer container, ref float y, string label, string value, string minusCommand, string plusCommand)
        {
            AddUiLabel(container, AdminUiName, label, 11, Anchor(0.04f, y, 0.52f, y + 0.034f), TextAnchor.MiddleLeft, "1 1 1 1");
            AddUiButton(container, AdminUiName, "-", minusCommand, Anchor(0.60f, y, 0.68f, y + 0.034f), "0.17 0.17 0.17 1");
            AddUiLabel(container, AdminUiName, value, 11, Anchor(0.70f, y, 0.82f, y + 0.034f), TextAnchor.MiddleCenter, "1 0.82 0.25 1");
            AddUiButton(container, AdminUiName, "+", plusCommand, Anchor(0.86f, y, 0.94f, y + 0.034f), "0.17 0.17 0.17 1");
            y -= 0.041f;
        }

        private void AddUiLabel(CuiElementContainer container, string parent, string text, int fontSize, string anchors, TextAnchor align, string color)
        {
            string[] parts = anchors.Split('|');
            AddUiLabel(container, parent, text, fontSize, parts[0], parts[1], align, color);
        }

        private void AddUiLabel(CuiElementContainer container, string parent, string text, int fontSize, string anchorMin, string anchorMax, TextAnchor align, string color)
        {
            container.Add(new CuiLabel
            {
                Text = { Text = text, FontSize = fontSize, Align = align, Color = color },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            }, parent);
        }

        private void AddUiButton(CuiElementContainer container, string parent, string text, string command, string anchors, string color)
        {
            string[] parts = anchors.Split('|');
            AddUiButton(container, parent, text, command, parts[0], parts[1], color);
        }

        private void AddUiButton(CuiElementContainer container, string parent, string text, string command, string anchorMin, string anchorMax, string color)
        {
            container.Add(new CuiButton
            {
                Button = { Color = color, Command = command },
                Text = { Text = text, FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            }, parent);
        }

        private string Anchor(float x1, float y1, float x2, float y2)
        {
            return x1.ToString("0.###", CultureInfo.InvariantCulture) + " " + y1.ToString("0.###", CultureInfo.InvariantCulture) + "|" + x2.ToString("0.###", CultureInfo.InvariantCulture) + " " + y2.ToString("0.###", CultureInfo.InvariantCulture);
        }

        [ConsoleCommand("treasurehunt.ui")]
        private void CcmdTreasureUi(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg == null ? null : arg.Player();
            if (player == null || !HasAdminAccess(player)) return;

            string action = arg.Args != null && arg.Args.Length > 0 ? arg.Args[0].ToString().ToLowerInvariant() : "open";
            if (action == "close")
            {
                DestroyAdminUi(player);
                return;
            }

            if (action == "lang_ru") config.General.Language = "ru";
            else if (action == "lang_en") config.General.Language = "en";
            else if (action == "lang_auto") config.General.Language = "auto";
            else if (action == "toggle_markers") config.Markers.Enabled = !config.Markers.Enabled;
            else if (action == "toggle_ddraw_text") config.Markers.DdrawTextEnabled = !config.Markers.DdrawTextEnabled;
            else if (action == "toggle_ddraw_box") config.Markers.DdrawBoxEnabled = !config.Markers.DdrawBoxEnabled;
            else if (action == "toggle_cui_hud") config.Markers.CuiHudEnabled = !config.Markers.CuiHudEnabled;
            else if (action == "toggle_avoid_water") config.Spawn.AvoidWater = !config.Spawn.AvoidWater;
            else if (action == "toggle_terrain_only") config.Spawn.TerrainOnly = !config.Spawn.TerrainOnly;
            else if (action == "toggle_water_topology") config.Spawn.RejectWaterTopology = !config.Spawn.RejectWaterTopology;
            else if (action == "waterdist_minus") config.Spawn.MinDistanceFromWater = Mathf.Max(0f, config.Spawn.MinDistanceFromWater - 5f);
            else if (action == "waterdist_plus") config.Spawn.MinDistanceFromWater += 5f;
            else if (action == "height_minus") config.Markers.TextHeight = Mathf.Max(0.1f, config.Markers.TextHeight - 0.5f);
            else if (action == "height_plus") config.Markers.TextHeight += 0.5f;
            else if (action == "textsize_minus") config.Markers.DdrawTextSize = Mathf.Max(6f, config.Markers.DdrawTextSize - 1f);
            else if (action == "textsize_plus") config.Markers.DdrawTextSize = Mathf.Min(40f, config.Markers.DdrawTextSize + 1f);
            else if (action == "box_minus") config.Markers.BoxSize = Mathf.Max(0.1f, config.Markers.BoxSize - 0.5f);
            else if (action == "box_plus") config.Markers.BoxSize += 0.5f;
            else if (action == "lifetime_minus") config.Chest.LifetimeMinutes = Mathf.Max(0f, config.Chest.LifetimeMinutes - 5f);
            else if (action == "lifetime_plus") config.Chest.LifetimeMinutes += 5f;
            else if (action == "maxchests_minus") config.Chest.MaxActiveChestsPerPlayer = Mathf.Max(1, config.Chest.MaxActiveChestsPerPlayer - 1);
            else if (action == "maxchests_plus") config.Chest.MaxActiveChestsPerPlayer += 1;
            else if (action == "mindist_minus") config.Spawn.MinTreasureDistance = Mathf.Max(0f, config.Spawn.MinTreasureDistance - 25f);
            else if (action == "mindist_plus") config.Spawn.MinTreasureDistance = Mathf.Min(config.Spawn.MaxTreasureDistance - 1f, config.Spawn.MinTreasureDistance + 25f);
            else if (action == "maxdist_minus") config.Spawn.MaxTreasureDistance = Mathf.Max(config.Spawn.MinTreasureDistance + 1f, config.Spawn.MaxTreasureDistance - 25f);
            else if (action == "maxdist_plus") config.Spawn.MaxTreasureDistance += 25f;

            SaveConfig();
            RedrawAllTreasureVisuals();
            OpenAdminUi(player);
        }

        private void RedrawAllTreasureVisuals()
        {
            foreach (BasePlayer active in BasePlayer.activePlayerList)
            {
                ClearMarkers(active);
                RefreshMarkers(active.userID);
                RefreshTreasureHud(active);
            }
        }

        #endregion

        #region Commands

        private void CmdTreasure(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            if (args == null || args.Length == 0 || string.Equals(args[0], "help", StringComparison.OrdinalIgnoreCase))
            {
                Reply(player, "Help", command);
                return;
            }

            string sub = args[0].ToLowerInvariant();
            if (sub == "clearhud" || sub == "marker" || sub == "markers")
            {
                ClearMarkers(player);
                HideTreasureHud(player);
                Reply(player, "HudHidden", command);
                return;
            }

            if (sub == "hud" || sub == "showhud")
            {
                hiddenHudUsers.Remove(player.userID);
                RefreshTreasureHud(player);
                Reply(player, "HudShown");
                return;
            }

            if (!HasAdminAccess(player))
            {
                Reply(player, "NoPermission");
                return;
            }

            if (sub == "ui" || sub == "settings" || sub == "config")
            {
                OpenAdminUi(player);
                return;
            }

            if (sub == "name" || sub == "chestname")
            {
                SetChestNameCommand(player, command, args);
                return;
            }

            if (sub == "give")
            {
                TreasureType type;
                if (args.Length < 2 || !TryParseType(args[1], out type))
                {
                    Reply(player, "InvalidMapType");
                    return;
                }

                GiveMap(player, type, false);
                Reply(player, "MapGiven", GetMapName(type, GetLanguage(player)));
                return;
            }

            if (sub == "clear")
            {
                ClearPlayerTreasures(player);
                return;
            }

            if (sub == "clearall")
            {
                ClearAllTreasures(player);
                return;
            }

            Reply(player, "Help", command);
        }

        private void CmdGetMap(BasePlayer player, string command, string[] args)
        {
            GiveMapAdmin(player, TreasureType.Normal);
        }

        private void CmdGetRareMap(BasePlayer player, string command, string[] args)
        {
            GiveMapAdmin(player, TreasureType.Rare);
        }

        private void CmdGetEpicMap(BasePlayer player, string command, string[] args)
        {
            GiveMapAdmin(player, TreasureType.Epic);
        }

        private void CmdGetLegendaryMap(BasePlayer player, string command, string[] args)
        {
            GiveMapAdmin(player, TreasureType.Legendary);
        }

        private void GiveMapAdmin(BasePlayer player, TreasureType type)
        {
            if (player == null) return;
            if (!HasAdminAccess(player))
            {
                Reply(player, "NoPermission");
                return;
            }

            GiveMap(player, type, false);
            Reply(player, "MapGiven", GetMapName(type, GetLanguage(player)));
        }

        private void SetChestNameCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (args == null || args.Length < 4)
            {
                Reply(player, "NameCommandHelp", command);
                return;
            }

            string langArg = args[1].ToLowerInvariant();
            bool english;
            if (langArg == "ru" || langArg == "rus" || langArg == "рус") english = false;
            else if (langArg == "en" || langArg == "eng") english = true;
            else
            {
                Reply(player, "NameCommandHelp", command);
                return;
            }

            TreasureType type;
            if (!TryParseType(args[2], out type))
            {
                Reply(player, "InvalidMapType");
                return;
            }

            string newName = string.Join(" ", args, 3, args.Length - 3).Trim();
            if (string.IsNullOrEmpty(newName))
            {
                Reply(player, "NameCommandHelp", command);
                return;
            }

            Dictionary<string, string> dict = english ? config.Chest.ChestNamesEn : config.Chest.ChestNamesRu;
            dict[TypeKey(type)] = newName;
            SaveConfig();
            RedrawAllTreasureVisuals();
            Reply(player, "NameUpdated", TypeKey(type), newName);
        }

        private void CmdClearTreasure(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (!HasAdminAccess(player))
            {
                Reply(player, "NoPermission");
                return;
            }

            ClearPlayerTreasures(player);
        }

        private void CmdClearHud(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            ClearMarkers(player);
            HideTreasureHud(player);
            Reply(player, "HudHidden", command);
        }

        private void ClearPlayerTreasures(BasePlayer player)
        {
            if (player == null) return;

            PlayerData pd;
            bool hasPendingSearch = GetPendingTreasureSearchCount(player.userID) > 0;
            if ((!players.TryGetValue(player.userID, out pd) || pd == null || pd.Chests == null || pd.Chests.Count == 0) && !hasPendingSearch)
            {
                Reply(player, "NoActiveChests");
                return;
            }

            if (pd != null && pd.Chests != null)
            {
                for (int i = pd.Chests.Count - 1; i >= 0; i--)
                {
                    BaseEntity chest = pd.Chests[i];
                    if (chest != null && !chest.IsDestroyed)
                        chest.Kill();
                }
            }

            pendingTreasureSearches.Remove(player.userID);
            players.Remove(player.userID);
            RefreshMarkers(player.userID);
            ClearMarkers(player);
            DestroyTreasureHud(player);
            Reply(player, "PlayerChestsCleared");
        }

        private void ClearAllTreasures(BasePlayer player)
        {
            List<BaseEntity> toKill = new List<BaseEntity>();
            foreach (PlayerData pd in players.Values)
            {
                if (pd == null || pd.Chests == null) continue;
                for (int i = 0; i < pd.Chests.Count; i++)
                {
                    BaseEntity chest = pd.Chests[i];
                    if (chest != null && !chest.IsDestroyed)
                        toKill.Add(chest);
                }
            }

            for (int i = 0; i < toKill.Count; i++)
                toKill[i].Kill();

            players.Clear();
            chestTypes.Clear();
            chestOwners.Clear();
            pendingTreasureSearches.Clear();

            foreach (BasePlayer active in BasePlayer.activePlayerList)
            {
                ClearMarkers(active);
                DestroyTreasureHud(active);
            }

            if (player != null)
                Reply(player, "AllChestsCleared");
        }

        #endregion

        #region API

        // вызов из другого плагина:
        // TreasureHunt?.Call("API_GiveMap", player, "normal"); // normal/rare/epic/legendary
        private object API_GiveMap(BasePlayer player, string typeName)
        {
            if (player == null) return false;

            TreasureType type;
            if (!TryParseType(typeName, out type))
                type = TreasureType.Normal;

            GiveMap(player, type);
            return true;
        }

        private object API_GiveMap(BasePlayer player, int typeIndex)
        {
            if (player == null) return false;

            int clamped = Mathf.Clamp(typeIndex, 0, 3);
            GiveMap(player, (TreasureType)clamped);
            return true;
        }

        #endregion

        #region Helpers

        private bool HasAdminAccess(BasePlayer player)
        {
            if (player == null) return false;
            if (config.General.ServerAdminsHaveAccess && player.IsAdmin) return true;
            return permission.UserHasPermission(player.UserIDString, config.General.AdminPermission);
        }

        private BasePlayer FindLootingPlayer(ResearchTable table)
        {
            if (table == null || table.net == null) return null;

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null || player.IsDead()) continue;
                if (player.inventory == null || player.inventory.loot == null) continue;
                if (!player.inventory.loot.IsLooting()) continue;

                BaseEntity source = player.inventory.loot.entitySource;
                if (source == table)
                    return player;

                if (source != null && source.net != null && source.net.ID == table.net.ID)
                    return player;
            }

            return null;
        }

        private bool MatchesContainerRule(string shortName, ContainerDropRule rule)
        {
            if (rule == null || string.IsNullOrEmpty(rule.ShortPrefabName)) return false;

            string source = (shortName ?? string.Empty).ToLowerInvariant();
            string target = rule.ShortPrefabName.ToLowerInvariant();
            string mode = (rule.MatchMode ?? "exact").ToLowerInvariant();

            if (mode == "contains") return source.Contains(target);
            if (mode == "startswith") return source.StartsWith(target);
            return source == target;
        }

        private bool Chance(float chance)
        {
            float value = chance > 1f ? chance / 100f : chance;
            value = Mathf.Clamp01(value);
            return UnityEngine.Random.Range(0f, 1f) <= value;
        }

        private bool TryParseType(string value, out TreasureType type)
        {
            type = TreasureType.Normal;
            if (string.IsNullOrEmpty(value)) return false;

            switch (value.Trim().ToLowerInvariant())
            {
                case "normal":
                case "common":
                case "n":
                case "обычная":
                case "обычный":
                    type = TreasureType.Normal;
                    return true;

                case "rare":
                case "r":
                case "редкая":
                case "редкий":
                    type = TreasureType.Rare;
                    return true;

                case "epic":
                case "e":
                case "эпическая":
                case "эпический":
                    type = TreasureType.Epic;
                    return true;

                case "legendary":
                case "leg":
                case "l":
                case "легендарная":
                case "легендарный":
                    type = TreasureType.Legendary;
                    return true;
            }

            return false;
        }

        private string TypeKey(TreasureType type)
        {
            switch (type)
            {
                case TreasureType.Rare:
                    return "rare";
                case TreasureType.Epic:
                    return "epic";
                case TreasureType.Legendary:
                    return "legendary";
                default:
                    return "normal";
            }
        }

        private string GetLanguage(BasePlayer player)
        {
            string configured = (config.General.Language ?? "ru").Trim().ToLowerInvariant();
            if (configured == "auto" && player != null)
            {
                string playerLang = lang.GetLanguage(player.UserIDString);
                if (!string.IsNullOrEmpty(playerLang) && playerLang.ToLowerInvariant().StartsWith("en"))
                    return "en";
                return "ru";
            }

            return configured.StartsWith("en") ? "en" : "ru";
        }

        private string GetMapName(TreasureType type, string language)
        {
            string key = TypeKey(type);
            Dictionary<string, string> names = language == "en" ? config.MapText.MapNamesEn : config.MapText.MapNamesRu;

            string name;
            if (names != null && names.TryGetValue(key, out name) && !string.IsNullOrEmpty(name))
                return name;

            return language == "en" ? key + " treasure map" : key + " карта сокровищ";
        }

        private string GetMapDescription(TreasureType type, string language)
        {
            string key = TypeKey(type);
            Dictionary<string, string> descriptions = language == "en" ? config.MapText.MapDescriptionsEn : config.MapText.MapDescriptionsRu;

            string description;
            if (descriptions != null && descriptions.TryGetValue(key, out description) && !string.IsNullOrEmpty(description))
                return description;

            return language == "en" ? config.MapText.NoteBodyEn : config.MapText.NoteBodyRu;
        }

        private string GetRarityName(TreasureType type, BasePlayer player)
        {
            string language = GetLanguage(player);
            string key = TypeKey(type);
            Dictionary<string, string> names = language == "en" ? config.MapText.RarityNamesEn : config.MapText.RarityNamesRu;

            string name;
            if (names != null && names.TryGetValue(key, out name) && !string.IsNullOrEmpty(name))
                return name;

            return key;
        }

        private string GetChestName(TreasureType type, BasePlayer player)
        {
            string language = GetLanguage(player);
            string key = TypeKey(type);
            Dictionary<string, string> names = language == "en" ? config.Chest.ChestNamesEn : config.Chest.ChestNamesRu;

            string name;
            if (names != null && names.TryGetValue(key, out name) && !string.IsNullOrEmpty(name))
                return name;

            string rarity = GetRarityName(type, player);
            return language == "en" ? rarity + " chest" : UpperFirst(rarity) + " сундук";
        }

        private string UpperFirst(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (text.Length == 1) return text.ToUpperInvariant();
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        private string GetMessage(string key, BasePlayer player)
        {
            string language = GetLanguage(player);
            Dictionary<string, string> dict = language == "en" ? config.Messages.En : config.Messages.Ru;
            Dictionary<string, string> fallback = language == "en" ? config.Messages.Ru : config.Messages.En;

            string value;
            if (dict != null && dict.TryGetValue(key, out value))
                return value;
            if (fallback != null && fallback.TryGetValue(key, out value))
                return value;

            return key;
        }

        private void Reply(BasePlayer player, string key, params object[] args)
        {
            if (player == null) return;

            string message = GetMessage(key, player);
            if (args != null && args.Length > 0)
            {
                try { message = string.Format(message, args); }
                catch { }
            }

            if (config.General.UsePrefix)
            {
                string prefix = GetMessage("Prefix", player);
                if (!string.IsNullOrEmpty(prefix))
                    message = prefix + " " + message;
            }

            SendReply(player, message);
        }

        private void Shuffle<T>(IList<T> list)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                int randomIndex = UnityEngine.Random.Range(i, list.Count);
                T temp = list[i];
                list[i] = list[randomIndex];
                list[randomIndex] = temp;
            }
        }

        private void DebugLog(string text)
        {
            if (config != null && config.General != null && config.General.Debug)
                Puts("[Debug] " + text);
        }

        #endregion
    }
}
