using BepInEx.Configuration;
using UnityEngine;

namespace Lineage2Valheim
{
    /// <summary>
    /// A first-pass menu launcher and independent window shell. Window bodies are
    /// intentionally placeholders; inventory, crafting, and skill data remain
    /// connected to Valheim's systems in later implementation steps.
    /// </summary>
    internal sealed class LineageMenuUi
    {
        private enum MenuWindow
        {
            None,
            Inventory,
            Skills,
            Crafting,
            CharacterStatus
        }

        private const float IconSize = 44f;
        private const float IconGap = 6f;
        private const float ScreenMargin = 16f;
        private const int WindowId = 724031;

        private readonly ConfigEntry<KeyCode> _skillsShortcut;
        private readonly ConfigEntry<KeyCode> _craftingShortcut;
        private readonly ConfigEntry<KeyCode> _statusShortcut;
        private readonly LineageInventoryCanvas _inventoryCanvas = new LineageInventoryCanvas();
        private readonly LineageSkillsPanel _skillsPanel = new LineageSkillsPanel();
        private readonly CharacterStatusPanel _statusPanel = new CharacterStatusPanel();

        private MenuWindow _openWindow;
        private Rect _windowRect;
        private Texture2D _pixel;
        private Texture2D _disc;
        private GUIStyle _titleStyle;
        private GUIStyle _skillTitleStyle;
        private GUIStyle _closeStyle;
        private GUIStyle _bodyStyle;
        private bool _hasSavedCursorState;
        private bool _cursorWasVisible;
        private CursorLockMode _cursorLockBeforeOpen;

        internal LineageMenuUi(ConfigFile config)
        {
            _skillsShortcut = config.Bind("UI", "SkillsShortcut", KeyCode.F8,
                "Opens or closes the custom Skills window.");
            _craftingShortcut = config.Bind("UI", "CraftingShortcut", KeyCode.F9,
                "Opens or closes the custom Crafting window.");
            _statusShortcut = config.Bind("UI", "CharacterStatusShortcut", KeyCode.F10,
                "Opens or closes the custom Character Status window.");
        }

        internal bool IsOpen => _openWindow != MenuWindow.None;

        internal string DescribeShortcuts()
        {
            return $"Inventory=Tab, Skills={_skillsShortcut.Value}, " +
                $"Crafting={_craftingShortcut.Value}, Character Status={_statusShortcut.Value}";
        }

        internal void Update()
        {
            if (Player.m_localPlayer == null)
            {
                Close();
                _inventoryCanvas.Dispose();
                return;
            }

            if (_openWindow == MenuWindow.Inventory)
            {
                if (!_inventoryCanvas.HasCanvas)
                {
                    Close();
                    _inventoryCanvas.Dispose();
                }
                else
                    _inventoryCanvas.Update();
            }

            if (Input.GetKeyDown(_skillsShortcut.Value))
                Toggle(MenuWindow.Skills);
            else if (Input.GetKeyDown(_craftingShortcut.Value))
                Toggle(MenuWindow.Crafting);
            else if (Input.GetKeyDown(_statusShortcut.Value))
                Toggle(MenuWindow.CharacterStatus);
            else if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
                Close();
        }

        internal void OnGUI(Texture2D pixel)
        {
            if (Player.m_localPlayer == null || pixel == null)
                return;

            _pixel = pixel;
            if (_openWindow == MenuWindow.Inventory)
                _inventoryCanvas.OnGUI(Event.current);
            EnsureStyles();
            DrawLauncher();

            if (IsOpen && _openWindow != MenuWindow.Inventory)
            {
                KeepWindowOnScreen();
                _windowRect = GUI.Window(WindowId, _windowRect, DrawWindow, GUIContent.none, GUIStyle.none);
                KeepWindowOnScreen();
            }
        }

        internal void Close()
        {
            if (_openWindow == MenuWindow.None)
                return;

            if (_openWindow == MenuWindow.Inventory)
                _inventoryCanvas.Close();
            _openWindow = MenuWindow.None;
            RestoreCursor();
        }

        internal void Dispose()
        {
            Close();
            _inventoryCanvas.Dispose();
            _statusPanel.Dispose();
            if (_disc != null) UnityEngine.Object.Destroy(_disc);
            _disc = null;
        }

        internal void OpenInventoryFromGame()
        {
            Toggle(MenuWindow.Inventory);
        }

        private void Toggle(MenuWindow window)
        {
            // Crafting is the attached panel to the right of the inventory grid.
            if (window == MenuWindow.Crafting)
                window = MenuWindow.Inventory;
            if (_openWindow == window)
            {
                Close();
                return;
            }

            if (!IsOpen)
            {
                _cursorWasVisible = Cursor.visible;
                _cursorLockBeforeOpen = Cursor.lockState;
                _hasSavedCursorState = true;
            }

            if (_openWindow == MenuWindow.Inventory)
                _inventoryCanvas.Close();

            _openWindow = window;
            SetDefaultWindowRect(window);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (window == MenuWindow.Inventory)
                _inventoryCanvas.Open(Close);
        }

        private void RestoreCursor()
        {
            if (!_hasSavedCursorState)
                return;

            Cursor.lockState = _cursorLockBeforeOpen;
            Cursor.visible = _cursorWasVisible;
            _hasSavedCursorState = false;
        }

        private void SetDefaultWindowRect(MenuWindow window)
        {
            float width;
            float height;

            switch (window)
            {
                case MenuWindow.Inventory:
                    width = 900f;
                    height = 620f;
                    break;
                case MenuWindow.Skills:
                    width = 900f;
                    height = 650f;
                    break;
                case MenuWindow.Crafting:
                    width = 560f;
                    height = 440f;
                    break;
                case MenuWindow.CharacterStatus:
                    width = 900f;
                    height = 620f;
                    break;
                default:
                    width = 380f;
                    height = 460f;
                    break;
            }

            width = Mathf.Min(width, Mathf.Max(300f, Screen.width - 32f));
            height = Mathf.Min(height, Mathf.Max(220f, Screen.height - 32f));
            _windowRect = new Rect((Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f, width, height);
        }

        private void KeepWindowOnScreen()
        {
            _windowRect.width = Mathf.Min(_windowRect.width, Mathf.Max(300f, Screen.width - 24f));
            _windowRect.height = Mathf.Min(_windowRect.height, Mathf.Max(200f, Screen.height - 24f));
            _windowRect.x = Mathf.Clamp(_windowRect.x, 12f, Mathf.Max(12f, Screen.width - _windowRect.width - 12f));
            _windowRect.y = Mathf.Clamp(_windowRect.y, 12f, Mathf.Max(12f, Screen.height - _windowRect.height - 12f));
        }

        private void DrawLauncher()
        {
            var totalWidth = IconSize * 4f + IconGap * 3f;
            var panel = new Rect(Screen.width - totalWidth - ScreenMargin - 8f,
                Screen.height - IconSize - ScreenMargin - 10f,
                totalWidth + 16f, IconSize + 20f);
            DrawRect(panel, new Color(0.015f, 0.025f, 0.04f, 0.82f));
            DrawRect(new Rect(panel.x, panel.y, panel.width, 2f), new Color(0.62f, 0.48f, 0.26f, 0.9f));

            var windows = new[] { MenuWindow.Inventory, MenuWindow.Skills, MenuWindow.Crafting, MenuWindow.CharacterStatus };
            for (var i = 0; i < windows.Length; i++)
            {
                var button = new Rect(panel.x + 8f + i * (IconSize + IconGap), panel.y + 7f, IconSize, IconSize);
                var selected = _openWindow == windows[i] ||
                    (_openWindow == MenuWindow.Inventory && windows[i] == MenuWindow.Crafting);
                var hovered = button.Contains(Event.current.mousePosition);

                var clicked = GUI.Button(button, GUIContent.none, GUIStyle.none);
                DrawRect(button, selected
                    ? new Color(0.36f, 0.50f, 0.58f, 0.98f)
                    : (hovered ? new Color(0.30f, 0.39f, 0.43f, 0.98f) : new Color(0.14f, 0.20f, 0.24f, 0.96f)));
                DrawRect(new Rect(button.x + 2f, button.y + 2f, button.width - 4f, button.height - 4f),
                    new Color(0.035f, 0.05f, 0.065f, 0.98f));
                DrawIcon(windows[i], new Rect(button.x + 7f, button.y + 6f, button.width - 14f, button.height - 14f));

                if (clicked)
                    Toggle(windows[i]);

                if (hovered)
                    GUI.Label(new Rect(button.x - 100f, button.y - 26f, 160f, 22f),
                        $"{WindowTitle(windows[i])} [{ShortcutFor(windows[i])}]", _bodyStyle);
            }
        }

        private void DrawWindow(int id)
        {
            var bounds = new Rect(0f, 0f, _windowRect.width, _windowRect.height);
            bool styled = _openWindow == MenuWindow.Skills || _openWindow == MenuWindow.CharacterStatus;
            float titleHeight = styled ? 48f : 34f;
            DrawRect(bounds, styled ? new Color(0.055f, 0.072f, 0.073f, 0.99f) :
                new Color(0.025f, 0.035f, 0.045f, 0.98f));
            DrawRect(new Rect(0f, 0f, bounds.width, titleHeight),
                styled ? new Color(0.12f, 0.13f, 0.12f, 1f) : new Color(0.09f, 0.12f, 0.13f, 1f));
            DrawRect(new Rect(0f, 0f, bounds.width, 2f), new Color(0.72f, 0.56f, 0.31f, 1f));
            DrawRect(new Rect(0f, bounds.height - 2f, bounds.width, 2f), new Color(0.48f, 0.37f, 0.22f, 0.9f));
            if (styled)
            {
                DrawRect(new Rect(0f, titleHeight - 2f, bounds.width, 2f),
                    new Color(0.62f, 0.47f, 0.28f, 1f));
                DrawRect(new Rect(0f, 0f, 2f, bounds.height), new Color(0.62f, 0.47f, 0.28f, 1f));
                DrawRect(new Rect(bounds.width - 2f, 0f, 2f, bounds.height), new Color(0.62f, 0.47f, 0.28f, 1f));
            }

            GUI.Label(styled ? new Rect(60f, 7f, bounds.width - 120f, 34f) :
                new Rect(14f, 7f, bounds.width - 58f, 22f),
                _openWindow == MenuWindow.Skills ? "SKILLS & ACTIONS" :
                (_openWindow == MenuWindow.CharacterStatus ? "CHARACTER STATUS" : WindowTitle(_openWindow)),
                styled ? _skillTitleStyle : _titleStyle);
            if (GUI.Button(styled ? new Rect(bounds.width - 40f, 8f, 31f, 30f) :
                new Rect(bounds.width - 32f, 5f, 26f, 24f), "×", _closeStyle))
            {
                Close();
                return;
            }
            GUI.DragWindow(new Rect(0f, 0f, bounds.width - 44f, titleHeight));

            var content = styled ? new Rect(14f, 60f, bounds.width - 28f, bounds.height - 74f) :
                new Rect(14f, 48f, bounds.width - 28f, bounds.height - 62f);
            DrawRect(content, new Color(0.045f, 0.055f, 0.06f, 0.94f));

            if (_openWindow == MenuWindow.CharacterStatus)
            {
                _statusPanel.Draw(content, _pixel);
                return;
            }

            if (_openWindow == MenuWindow.Skills)
            {
                _skillsPanel.Draw(content, _pixel);
                return;
            }

            var message = "Crafting window shell\n\nRecipes, required materials, and nearby-station requirements will be connected here.";

            GUI.Label(new Rect(content.x + 18f, content.y + 18f, content.width - 36f, 100f), message, _bodyStyle);
        }

        private string ShortcutFor(MenuWindow window)
        {
            switch (window)
            {
                case MenuWindow.Inventory: return "Tab";
                case MenuWindow.Skills: return _skillsShortcut.Value.ToString();
                case MenuWindow.Crafting: return _craftingShortcut.Value.ToString();
                default: return _statusShortcut.Value.ToString();
            }
        }

        private string WindowTitle(MenuWindow window)
        {
            switch (window)
            {
                case MenuWindow.Inventory: return "Inventory";
                case MenuWindow.Skills: return "Skills and Actions";
                case MenuWindow.Crafting: return "Crafting";
                case MenuWindow.CharacterStatus: return "Character Status";
                default: return string.Empty;
            }
        }

        private void DrawIcon(MenuWindow window, Rect rect)
        {
            var gold = new Color(0.78f, 0.65f, 0.40f, 1f);
            var pale = new Color(0.80f, 0.84f, 0.82f, 1f);
            var accent = new Color(0.38f, 0.68f, 0.75f, 1f);

            switch (window)
            {
                case MenuWindow.Inventory:
                    DrawRect(new Rect(rect.x + 2f, rect.y + 8f, rect.width - 4f, rect.height - 10f), gold);
                    DrawRect(new Rect(rect.x, rect.y + 5f, rect.width, 7f), new Color(0.88f, 0.75f, 0.49f, 1f));
                    DrawRect(new Rect(rect.center.x - 2f, rect.y + 15f, 4f, 6f), new Color(0.18f, 0.16f, 0.12f, 1f));
                    DrawRect(new Rect(rect.x + 4f, rect.y + 22f, rect.width - 8f, 2f), new Color(0.45f, 0.34f, 0.20f, 1f));
                    break;
                case MenuWindow.Skills:
                    DrawRect(new Rect(rect.x + 4f, rect.y + 3f, rect.width - 8f, rect.height - 6f), pale);
                    DrawRect(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, 3f), new Color(0.18f, 0.24f, 0.25f, 1f));
                    DrawRect(new Rect(rect.x + 8f, rect.y + 15f, rect.width - 16f, 3f), new Color(0.18f, 0.24f, 0.25f, 1f));
                    DrawRect(new Rect(rect.x + 8f, rect.y + 22f, rect.width - 16f, 3f), accent);
                    DrawRect(new Rect(rect.x + 8f, rect.y + 29f, rect.width - 16f, 3f), new Color(0.18f, 0.24f, 0.25f, 1f));
                    break;
                case MenuWindow.Crafting:
                    var matrix = GUI.matrix;
                    GUIUtility.RotateAroundPivot(-42f, rect.center);
                    DrawRect(new Rect(rect.center.x - 2.5f, rect.y + 10f, 5f, rect.height - 9f), new Color(0.62f, 0.42f, 0.23f, 1f));
                    GUI.matrix = matrix;
                    DrawRect(new Rect(rect.x + 6f, rect.y + 2f, rect.width - 12f, 8f), pale);
                    DrawRect(new Rect(rect.x + 8f, rect.y + 10f, 7f, 7f), gold);
                    break;
                case MenuWindow.CharacterStatus:
                    DrawDisc(new Rect(rect.center.x - 6f, rect.y + 2f, 12f, 12f), pale);
                    DrawDisc(new Rect(rect.x + 3f, rect.y + 15f, rect.width - 6f, rect.height - 13f), accent);
                    DrawRect(new Rect(rect.center.x - 4f, rect.y + 16f, 8f, rect.height - 14f), new Color(0.22f, 0.42f, 0.49f, 1f));
                    break;
            }
        }

        private void DrawDisc(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _disc != null ? _disc : Texture2D.whiteTexture, ScaleMode.StretchToFill, true);
            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null)
                return;

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            _titleStyle.normal.textColor = new Color(0.93f, 0.89f, 0.79f, 1f);
            _skillTitleStyle = new GUIStyle(_titleStyle)
            {
                fontSize = 25,
                alignment = TextAnchor.MiddleCenter
            };
            _skillTitleStyle.normal.textColor = new Color(0.87f, 0.74f, 0.51f, 1f);

            _closeStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 2)
            };
            _closeStyle.normal.textColor = new Color(0.87f, 0.82f, 0.70f, 1f);
            _closeStyle.hover.textColor = Color.white;

            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft
            };
            _bodyStyle.normal.textColor = new Color(0.79f, 0.82f, 0.80f, 1f);

            _disc = new Texture2D(32, 32, TextureFormat.RGBA32, false)
            {
                name = "Lineage2Valheim.CharacterStatusIconDisc",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[32 * 32];
            for (var y = 0; y < 32; y++)
            {
                for (var x = 0; x < 32; x++)
                {
                    var dx = x - 15.5f;
                    var dy = y - 15.5f;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);
                    var alpha = Mathf.Clamp01(16f - distance);
                    pixels[y * 32 + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            _disc.SetPixels(pixels);
            _disc.Apply();
        }

        private void DrawRect(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _pixel, ScaleMode.StretchToFill, true);
            GUI.color = previous;
        }
    }
}
