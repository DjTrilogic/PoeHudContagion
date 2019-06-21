// ReSharper disable StringLiteralTypo
// ReSharper disable UnusedMember.Global

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Contagion.Utilities;
using ImGuiNET;
using PoeHUD.Framework.Helpers;
using PoeHUD.Models;
using PoeHUD.Models.Enums;
using PoeHUD.Plugins;
using PoeHUD.Poe.Components;
using PoeHUD.Poe.FilesInMemory;
using SharpDX;

namespace Contagion.Core
{
    public class ContagionPlugin : BaseSettingsPlugin<Settings>
    {
        private readonly Stopwatch _aimTimer = Stopwatch.StartNew();
        private readonly List<EntityWrapper> _entities = new List<EntityWrapper>();
        private Dictionary<string, StatsDat.StatRecord> _statRecords;
        private bool _aiming;
        private Vector2 _oldMousePos;
        private HashSet<string> _ignoredMonsters;

        private readonly string[] _ignoredBuffs = {
            "capture_monster_captured",
            "capture_monster_disappearing"
        };

        private readonly string[] _lightLessGrub =
            {
                "Metadata/Monsters/HuhuGrub/AbyssGrubMobile",
                "Metadata/Monsters/HuhuGrub/AbyssGrubMobileMinion"
        };
        
        private readonly string[] _raisedZombie =
        {
                "Metadata/Monsters/RaisedZombies/RaisedZombieStandard",
                "Metadata/Monsters/RaisedZombies/RaisedZombieMummy",
                "Metadata/Monsters/RaisedZombies/NecromancerRaisedZombieStandard"
        };

        private readonly string[] _summonedSkeleton =
        {
                "Metadata/Monsters/RaisedSkeletons/RaisedSkeletonStandard",
                "Metadata/Monsters/RaisedSkeletons/RaisedSkeletonStatue",
                "Metadata/Monsters/RaisedSkeletons/RaisedSkeletonMannequin",
                "Metadata/Monsters/RaisedSkeletons/RaisedSkeletonStatueMale",
                "Metadata/Monsters/RaisedSkeletons/RaisedSkeletonStatueGold",
                "Metadata/Monsters/RaisedSkeletons/RaisedSkeletonStatueGoldMale",
                "Metadata/Monsters/RaisedSkeletons/NecromancerRaisedSkeletonStandard",
                "Metadata/Monsters/RaisedSkeletons/TalismanRaisedSkeletonStandard"
        };


        public ContagionPlugin() => PluginName = "Contagion";

        public override void Initialise()
        {
            LoadIgnoredMonsters($@"{PluginDirectory}\Ignored Monsters.txt");
            _statRecords = GameController.Files.Stats.records;
        }

        public void LoadIgnoredMonsters(string fileName)
        {
            _ignoredMonsters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(fileName))
            {
                LogError($@"Failed to find {fileName}", 10);
                return;
            }

            File.ReadAllLines(fileName)
                .Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith("#"))
                .ForEach(x => _ignoredMonsters.Add(x.Trim().ToLower()));
        }

        public override void Render()
        {
            if (_aimTimer.ElapsedMilliseconds < 100)
            {
                return;
            }

            try
            {
                if (!Keyboard.IsKeyDown(2))
                {
                    _oldMousePos = Mouse.GetCursorPositionVector();
                }

                if (Keyboard.IsKeyDown(2)
                 && !GameController.Game.IngameState.IngameUi.InventoryPanel.IsVisible
                 && !GameController.Game.IngameState.IngameUi.OpenLeftPanel.IsVisible)
                {
                    _aiming = true;
                    var bestTarget = ScanValidMonsters()?.FirstOrDefault();
                    Attack(bestTarget);
                }

                if (!Keyboard.IsKeyDown(2) && _aiming)
                {
                    Mouse.SetCursorPosition(_oldMousePos);
                }
                _aiming = false;
            }
            catch (Exception e)
            {
                LogError("Something went wrong? " + e, 5);
            }

            _aimTimer.Restart();
        }

        public override void DrawSettingsMenu()
        {
            Settings.ShowAimRange.Value = ImGuiExtension.Checkbox("Display Aim Range", Settings.ShowAimRange.Value);
            Settings.AimRange.Value = ImGuiExtension.IntDrag("Target Distance", "%.00f units", Settings.AimRange);
            Settings.AimLoopDelay.Value = ImGuiExtension.IntDrag("Target Delay", "%.00f ms", Settings.AimLoopDelay);
            Settings.DebugMonsterWeight.Value = ImGuiExtension.Checkbox("Draw Weight Results On Monsters", Settings.DebugMonsterWeight.Value);
            Settings.RMousePos.Value =
                    ImGuiExtension.Checkbox("Restore Mouse Position After Letting Go Of Auto Aim Hotkey", Settings.RMousePos.Value);
            Settings.AimKey.Value = ImGuiExtension.HotkeySelector("Auto Aim Hotkey", "Auto Aim Popup", Settings.AimKey.Value);
            Settings.ContagionKey.Value = ImGuiExtension.HotkeySelector("Contagion Key", "Contagion Key", Settings.ContagionKey.Value);
            Settings.EssenceDrainKey.Value = ImGuiExtension.HotkeySelector("Essence Drain Key", "Essence Drain Key", Settings.EssenceDrainKey.Value);
            Settings.AimPlayers.Value = ImGuiExtension.Checkbox("Aim Players Instead?", Settings.AimPlayers.Value);
            ImGui.Separator();
            ImGui.BulletText("Weight Settings");
            PoeHUD.Hud.UI.ImGuiExtension.ToolTip("Aims monsters with higher weight first");
            Settings.UniqueRarityWeight.Value = ImGuiExtension.IntDrag("Unique Monster", Settings.UniqueRarityWeight.Value > 0 ? "+%.00f" : "%.00f",
                    Settings.UniqueRarityWeight);
            Settings.RareRarityWeight.Value = ImGuiExtension.IntDrag("Rare Monster", Settings.RareRarityWeight.Value > 0 ? "+%.00f" : "%.00f",
                    Settings.RareRarityWeight);
            Settings.MagicRarityWeight.Value = ImGuiExtension.IntDrag("Magic Monster", Settings.MagicRarityWeight.Value > 0 ? "+%.00f" : "%.00f",
                    Settings.MagicRarityWeight);
            Settings.NormalRarityWeight.Value = ImGuiExtension.IntDrag("Normal Monster", Settings.NormalRarityWeight.Value > 0 ? "+%.00f" : "%.00f",
                    Settings.NormalRarityWeight);
            Settings.CannotDieAura.Value =
                    ImGuiExtension.IntDrag("Cannot Die Aura", Settings.CannotDieAura.Value > 0 ? "+%.00f" : "%.00f", Settings.CannotDieAura);
            PoeHUD.Hud.UI.ImGuiExtension.ToolTip("Monster that holds the Cannot Die Arua");
            Settings.capture_monster_trapped.Value = ImGuiExtension.IntDrag("Monster In Net",
                    Settings.capture_monster_trapped.Value > 0 ? "+%.00f" : "%.00f", Settings.capture_monster_trapped);
            PoeHUD.Hud.UI.ImGuiExtension.ToolTip("Monster is currently in a net");
            Settings.capture_monster_enraged.Value = ImGuiExtension.IntDrag("Monster Broken Free From Net",
                    Settings.capture_monster_enraged.Value > 0 ? "+%.00f" : "%.00f", Settings.capture_monster_enraged);
            PoeHUD.Hud.UI.ImGuiExtension.ToolTip("Monster has recently broken free from the net");
            Settings.BeastHearts.Value =
                    ImGuiExtension.IntDrag("Malachai Hearts", Settings.BeastHearts.Value > 0 ? "+%.00f" : "%.00f", Settings.BeastHearts);
            Settings.TukohamaShieldTotem.Value = ImGuiExtension.IntDrag("Tukohama Shield Totem",
                    Settings.TukohamaShieldTotem.Value > 0 ? "+%.00f" : "%.00f", Settings.TukohamaShieldTotem);
            PoeHUD.Hud.UI.ImGuiExtension.ToolTip("Usually seen in the Tukahama Boss (Act 6)");
            Settings.StrongBoxMonster.Value = ImGuiExtension.IntDrag("Strongbox Monster (Experimental)",
                    Settings.StrongBoxMonster.Value > 0 ? "+%.00f" : "%.00f", Settings.StrongBoxMonster);
            Settings.BreachMonsterWeight.Value = ImGuiExtension.IntDrag("Breach Monster (Experimental)",
                    Settings.BreachMonsterWeight.Value > 0 ? "+%.00f" : "%.00f", Settings.BreachMonsterWeight);
            Settings.HarbingerMinionWeight.Value = ImGuiExtension.IntDrag("Harbinger Monster (Experimental)",
                    Settings.HarbingerMinionWeight.Value > 0 ? "+%.00f" : "%.00f", Settings.HarbingerMinionWeight);
            Settings.SummonedSkeoton.Value = ImGuiExtension.IntDrag("Summoned Skeleton", Settings.SummonedSkeoton.Value > 0 ? "+%.00f" : "%.00f",
                    Settings.SummonedSkeoton);
            Settings.RaisesUndead.Value =
                    ImGuiExtension.IntDrag("Raises Undead", Settings.RaisesUndead.Value > 0 ? "+%.00f" : "%.00f", Settings.RaisesUndead);
            Settings.RaisedZombie.Value =
                    ImGuiExtension.IntDrag("Raised Zombie", Settings.RaisedZombie.Value > 0 ? "+%.00f" : "%.00f", Settings.RaisedZombie);
            Settings.LightlessGrub.Value =
                    ImGuiExtension.IntDrag("Lightless Grub", Settings.LightlessGrub.Value > 0 ? "+%.00f" : "%.00f", Settings.LightlessGrub);
            PoeHUD.Hud.UI.ImGuiExtension.ToolTip("Usually seen in the Abyss, they are the little insects");
            Settings.TaniwhaTail.Value =
                    ImGuiExtension.IntDrag("Taniwha Tail", Settings.TaniwhaTail.Value > 0 ? "+%.00f" : "%.00f", Settings.TaniwhaTail);
            PoeHUD.Hud.UI.ImGuiExtension.ToolTip("Usually seen in the Kaom Stronghold Areas");
            Settings.DiesAfterTime.Value =
                    ImGuiExtension.IntDrag("Dies After Time", Settings.DiesAfterTime.Value > 0 ? "+%.00f" : "%.00f", Settings.DiesAfterTime);
            PoeHUD.Hud.UI.ImGuiExtension.ToolTip("If the Monster dies soon, Usually this is a totem that was summoned");
            base.DrawSettingsMenu();
        }

        public override void EntityAdded(EntityWrapper entityWrapper) { _entities.Add(entityWrapper); }

        public override void EntityRemoved(EntityWrapper entityWrapper) { _entities.Remove(entityWrapper); }

        private void Attack(Tuple<float, EntityWrapper> bestTarget)
        {
            if (bestTarget == null)
            {
                return;
            }

            var position = GameController.Game.IngameState.Camera.WorldToScreen(bestTarget.Item2.Pos.Translate(0, 0, 0), bestTarget.Item2);
            var windowRectangle = GameController.Window.GetWindowRectangle();
            if (!position.IsInside(windowRectangle))
            {
                return;
            }

            var offset = GameController.Window.GetWindowRectangle().TopLeft;
            Mouse.SetCursorPos(position + offset);

            Keyboard.KeyPress(bestTarget.Item2.HasBuff("contagion", true) ? Settings.EssenceDrainKey.Value : Settings.ContagionKey.Value);
        }

        private IEnumerable<Tuple<float, EntityWrapper>> ScanValidMonsters()
        {
            return _entities?.Where(x => x.HasComponent<Monster>()
                                         && x.IsAlive
                                         && x.IsHostile
                                         && x.GetStatValue("ignored_by_enemy_target_selection", _statRecords) == 0
                                         && x.GetStatValue("cannot_die", _statRecords) == 0
                                         && x.GetStatValue("cannot_be_damaged", _statRecords) == 0
                                         && !_ignoredBuffs.Any(b => x.HasBuff(b))
                                         && !_ignoredMonsters.Any(im => x.Path.ToLower().Contains(im))
                                         )
                .Select(x => new Tuple<float, EntityWrapper>(ComputeWeight(x), x))
                .Where(x=> x.Item1< Settings.AimRange)
                .OrderByDescending(x => x.Item1);
        }

        public float ComputeWeight(EntityWrapper entity)
        {
            int weight = 0;
            weight -= API.GameController.Player.DistanceFrom(entity) / 10;

            if (entity.GetComponent<Life>().HasBuff("capture_monster_trapped")) weight += Settings.capture_monster_trapped;
            if (entity.GetComponent<Life>().HasBuff("harbinger_minion_new")) weight += Settings.HarbingerMinionWeight;
            if (entity.GetComponent<Life>().HasBuff("capture_monster_enraged")) weight += Settings.capture_monster_enraged;
            if (entity.Path.Contains("/BeastHeart")) weight += Settings.BeastHearts;
            if (entity.Path == "Metadata/Monsters/Tukohama/TukohamaShieldTotem") weight += Settings.TukohamaShieldTotem;

            switch (entity.GetComponent<ObjectMagicProperties>().Rarity)
            {
                case MonsterRarity.Unique:
                    weight += Settings.UniqueRarityWeight;
                    break;
                case MonsterRarity.Rare:
                    weight += Settings.RareRarityWeight;
                    break;
                case MonsterRarity.Magic:
                    weight += Settings.MagicRarityWeight;
                    break;
                case MonsterRarity.White:
                    weight += Settings.NormalRarityWeight;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (entity.HasComponent<DiesAfterTime>()) weight += Settings.DiesAfterTime;
            if (_summonedSkeleton.Any(path => entity.Path == path)) weight += Settings.SummonedSkeoton;
            if (_raisedZombie.Any(path => entity.Path == path)) weight += Settings.RaisedZombie;
            if (_lightLessGrub.Any(path => entity.Path == path)) weight += Settings.LightlessGrub;
            if (entity.Path.Contains("TaniwhaTail")) weight += Settings.TaniwhaTail;
            return weight;
        }
    }

}