using System.Globalization;
using UnityEngine;

namespace Lineage2Valheim
{
    /// <summary>Compact Lineage-style player vitals and food in one painted HUD frame.</summary>
    internal sealed class LineageVitalsPanel
    {
        private const float DefaultX = 18f;
        private const float DefaultY = 82f;
        private const float Width = 320f;
        private const float Height = 164f;
        private const string PositionPrefix = "Lineage2Valheim.Vitals.";
        private static readonly Color HealthColor = new Color(0.66f, 0.20f, 0.16f, 1f);
        private static readonly Color StaminaColor = new Color(0.73f, 0.54f, 0.17f, 1f);
        private static readonly Color EitrColor = new Color(0.20f, 0.42f, 0.68f, 1f);
        private GUIStyle _nameStyle;
        private GUIStyle _barStyle;
        private GUIStyle _foodStyle;
        private GUIStyle _foodTimerStyle;
        private Player _owner;
        private Vector2 _position = new Vector2(DefaultX, DefaultY);
        private Vector2 _grabOffset;
        private bool _moving;

        internal void Update(Player player, bool cursorAvailable)
        {
            if (player == null)
            {
                _owner = null;
                _moving = false;
                return;
            }
            if (_owner != player) LoadPosition(player);
            _position = ClampPosition(_position);
            Vector2 mouse = new Vector2(Input.mousePosition.x,
                Screen.height - Input.mousePosition.y);
            if (cursorAvailable && Input.GetMouseButtonDown(0) && GripRect().Contains(mouse))
            {
                _moving = true;
                _grabOffset = mouse - _position;
            }
            if (!_moving) return;
            if (Input.GetMouseButton(0))
                _position = ClampPosition(mouse - _grabOffset);
            if (Input.GetMouseButtonUp(0))
            {
                _moving = false;
                SavePosition();
            }
        }

        private void LoadPosition(Player player)
        {
            _owner = player;
            _moving = false;
            _position = new Vector2(DefaultX, DefaultY);
            var data = player.m_customData;
            if (data == null ||
                !data.TryGetValue(PositionPrefix + "X", out string x) ||
                !data.TryGetValue(PositionPrefix + "Y", out string y) ||
                !float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out float px) ||
                !float.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out float py)) return;
            _position = new Vector2(px, py);
        }

        private void SavePosition()
        {
            var data = _owner?.m_customData;
            if (data == null) return;
            data[PositionPrefix + "X"] = _position.x.ToString("R", CultureInfo.InvariantCulture);
            data[PositionPrefix + "Y"] = _position.y.ToString("R", CultureInfo.InvariantCulture);
        }

        private static Vector2 ClampPosition(Vector2 position) => new Vector2(
            Mathf.Clamp(position.x, 10f, Mathf.Max(10f, Screen.width - Width)),
            Mathf.Clamp(position.y, 8f, Mathf.Max(8f, Screen.height - Height)));

        private Rect GripRect() => new Rect(_position.x - 7f, _position.y + 40f, 16f, 74f);

        internal void Draw(Player player, Texture2D pixel, LineageHotbars art)
        {
            if (player == null || pixel == null || art == null) return;
            EnsureStyles();
            var oldColor = GUI.color;
            GUI.color = Color.white;
            float x = _position.x;
            float y = _position.y;
            var panel = new Rect(x, y, Width, Height);
            art.DrawStyledFrame(panel, pixel);
            art.DrawVerticalGrip(GripRect(), pixel);
            GUI.Label(new Rect(x + 14f, y + 9f, Width - 28f, 22f),
                player.GetPlayerName(), _nameStyle);

            DrawBar(new Rect(x + 11f, y + 36f, Width - 22f, 21f), "HP",
                player.GetHealth(), player.GetMaxHealth(), HealthColor, pixel, art);
            DrawBar(new Rect(x + 11f, y + 61f, Width - 22f, 21f), "SP",
                player.GetStamina(), player.GetMaxStamina(), StaminaColor, pixel, art);
            DrawBar(new Rect(x + 11f, y + 86f, Width - 22f, 21f), "EITR",
                player.GetEitr(), player.GetMaxEitr(), EitrColor, pixel, art);

            var foods = player.GetFoods();
            int activeFoodCount = foods != null ? foods.Count : 0;
            GUI.Label(new Rect(x + 16f, y + 119f, 62f, 25f),
                $"FOOD {activeFoodCount}/3", _foodStyle);
            for (int i = 0; i < 3; i++)
            {
                var slot = new Rect(x + 81f + i * 43f, y + 115f, 38f, 38f);
                art.DrawStyledSlot(slot, pixel);
                if (foods == null || i >= foods.Count || foods[i].m_item == null) continue;
                var icon = foods[i].m_item.GetIcon();
                if (icon != null)
                    DrawSpriteIcon(icon, new Rect(slot.x + 4f, slot.y + 3f, 30f, 27f));
                Fill(new Rect(slot.x + 2f, slot.yMax - 12f, slot.width - 4f, 10f),
                    new Color(0.025f, 0.03f, 0.035f, 0.9f), pixel);
                GUI.Label(new Rect(slot.x + 2f, slot.yMax - 13f, slot.width - 4f, 12f),
                    Mathf.CeilToInt(Mathf.Max(0f, foods[i].m_time) / 60f) + "m", _foodTimerStyle);
            }
            GUI.color = oldColor;
        }

        private void DrawBar(Rect rect, string label, float current, float maximum,
            Color color, Texture2D pixel, LineageHotbars art)
        {
            art.DrawStyledFrame(rect, pixel);
            var inner = new Rect(rect.x + 6f, rect.y + 5f, rect.width - 12f, rect.height - 10f);
            Fill(inner, new Color(0.055f, 0.065f, 0.066f, 0.96f), pixel);
            float fillWidth = inner.width * Mathf.Clamp01(maximum > 0f ? current / maximum : 0f);
            if (fillWidth > 0f)
            {
                Fill(new Rect(inner.x, inner.y, fillWidth, inner.height), color, pixel);
                Fill(new Rect(inner.x, inner.y, fillWidth, 2f),
                    Color.Lerp(color, Color.white, 0.25f), pixel);
            }
            GUI.Label(new Rect(rect.x + 11f, rect.y + 1f, 56f, rect.height - 2f),
                label, _barStyle);
            GUI.Label(new Rect(rect.x + 75f, rect.y + 1f, rect.width - 86f, rect.height - 2f),
                Mathf.CeilToInt(current) + " / " + Mathf.CeilToInt(maximum), _barStyle);
        }

        private static void DrawSpriteIcon(Sprite sprite, Rect destination)
        {
            if (sprite == null || sprite.texture == null) return;
            Rect source = sprite.textureRect;
            var uv = new Rect(source.x / sprite.texture.width,
                source.y / sprite.texture.height,
                source.width / sprite.texture.width,
                source.height / sprite.texture.height);
            GUI.DrawTextureWithTexCoords(destination, sprite.texture, uv, true);
        }

        private void EnsureStyles()
        {
            if (_nameStyle != null) return;
            _nameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip
            };
            _nameStyle.normal.textColor = new Color(0.91f, 0.83f, 0.64f, 1f);
            _barStyle = new GUIStyle(_nameStyle)
            {
                fontSize = 11, alignment = TextAnchor.MiddleLeft
            };
            _barStyle.normal.textColor = Color.white;
            _foodStyle = new GUIStyle(_nameStyle)
            {
                fontSize = 10, alignment = TextAnchor.MiddleLeft
            };
            _foodTimerStyle = new GUIStyle(_barStyle)
            {
                fontSize = 8,
                alignment = TextAnchor.MiddleCenter
            };
        }

        private static void Fill(Rect rect, Color color, Texture2D pixel)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, pixel);
            GUI.color = previous;
        }
    }
}
