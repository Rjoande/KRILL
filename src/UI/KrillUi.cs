using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KRILL.UI
{
	/// <summary>
	/// Code-built UGUI factory. Ported from KRAB's KrabUi.cs verbatim (design doc
	/// §6: KRILL shares KRAB's skin on purpose — same ecosystem, same "feels like
	/// stock" goal) — palette, metrics and behavior are identical by design, not
	/// by coincidence; keep the two in sync if the shared look changes.
	/// </summary>
	public static class KrillUi
	{
		// Palette
		public static readonly Color Win = FromHex("262a30");
		public static readonly Color Panel = FromHex("2f343b");
		public static readonly Color Panel2 = FromHex("363c44");
		public static readonly Color Inset = FromHex("1b1e23");
		public static readonly Color Line = FromHex("454c56");
		public static readonly Color HeadA = FromHex("3b424c");
		public static readonly Color Tan = FromHex("d7ddc4");
		public static readonly Color TanDim = FromHex("a3ab8f");
		public static readonly Color Text = FromHex("ccd0d5");
		public static readonly Color Muted = FromHex("aeb3ba");
		public static readonly Color Faint = FromHex("90959c");
		public static readonly Color Green = FromHex("9dc23e");
		public static readonly Color GreenHi = FromHex("b9e34f");
		public static readonly Color Malachite = FromHex("c3ea54");
		public static readonly Color Warn = FromHex("d9a441");
		public static readonly Color Danger = FromHex("c8724c");

		private static Font font;

		public static Font Font
		{
			get
			{
				if (font == null)
				{
					font = UnityEngine.Font.CreateDynamicFontFromOSFont(
						new[] { "Segoe UI", "Verdana", "Arial" }, 14);
				}
				return font;
			}
		}

		private static Color FromHex(string hex)
		{
			byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
			byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
			byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
			return new Color32(r, g, b, 255);
		}

		public static GameObject Go(string name, Transform parent)
		{
			GameObject go = new GameObject(name, typeof(RectTransform));
			go.transform.SetParent(parent, false);
			return go;
		}

		public static Image Panel_(string name, Transform parent, Color color)
		{
			Image image = Go(name, parent).AddComponent<Image>();
			image.color = color;
			return image;
		}

		/// <summary>Panel with a 1px border: border-colored back + inset front. The fill child ignores layout groups so a layout can live on the panel itself.</summary>
		public static RectTransform Bordered(string name, Transform parent, Color fill, Color border)
		{
			Image back = Panel_(name, parent, border);
			Image front = Panel_("Fill", back.transform, fill);
			Stretch(front.rectTransform, 1f);
			front.raycastTarget = false;
			front.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
			return back.rectTransform;
		}

		public static void Stretch(RectTransform rect, float margin = 0f)
		{
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = new Vector2(margin, margin);
			rect.offsetMax = new Vector2(-margin, -margin);
		}

		public static Text Label(Transform parent, string text, int size, Color color,
			TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
		{
			Text label = Go("Label", parent).AddComponent<Text>();
			label.font = Font;
			label.fontSize = size;
			label.fontStyle = style;
			label.color = color;
			label.alignment = anchor;
			label.horizontalOverflow = HorizontalWrapMode.Overflow;
			label.verticalOverflow = VerticalWrapMode.Overflow;
			label.text = text;
			label.raycastTarget = false;
			return label;
		}

		public static Button TextButton(Transform parent, string text, UnityAction onClick,
			Color background, Color textColor, int fontSize = 13, float width = 0f, float height = 24f)
		{
			Image image = Panel_("Button", parent, background);
			Button button = image.gameObject.AddComponent<Button>();
			button.targetGraphic = image;
			ColorBlock colors = button.colors;
			colors.normalColor = Color.white;
			colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
			colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
			button.colors = colors;
			button.onClick.AddListener(onClick);
			HorizontalLayoutGroup inner = image.gameObject.AddComponent<HorizontalLayoutGroup>();
			inner.padding = new RectOffset(10, 10, 3, 3);
			inner.childAlignment = TextAnchor.MiddleCenter;
			inner.childForceExpandWidth = false;
			inner.childForceExpandHeight = false;
			inner.childControlWidth = true;
			inner.childControlHeight = true;
			Label(image.transform, text, fontSize, textColor, TextAnchor.MiddleCenter);
			LayoutElement le = image.gameObject.AddComponent<LayoutElement>();
			le.preferredHeight = height;
			if (width > 0f)
			{
				le.preferredWidth = width;
			}
			return button;
		}

		/// <summary>
		/// Same visual as TextButton, but drives separate press/release callbacks
		/// (PointerDown/PointerUp) instead of a single onClick — for controls that
		/// must track "held", not "clicked" (Hold-kind groups' Trigger button,
		/// 2026-08-19). Unity's EventSystem keeps delivering PointerUp to the
		/// object that received PointerDown even if the cursor has moved off it
		/// by release time, so a drag-off-then-release still calls onRelease.
		///
		/// The release is ALSO guaranteed by the button's own lifecycle
		/// (HoldTracker.OnDisable, 2026-09-02): if the button is destroyed or
		/// hidden while pressed — a content rebuild, the window closing, a scene
		/// change — it releases itself before going away, so a press can never
		/// be orphaned by losing the object that would have received the
		/// PointerUp. Same pattern FocusLock/TypingLock below already use for
		/// their control locks. Still adds a Button (colors only, onClick left
		/// unwired) purely for the same hover/press visual feedback as every
		/// other button in this UI.
		/// </summary>
		public static void HoldButton(Transform parent, string text, UnityAction onPress, UnityAction onRelease,
			Color background, Color textColor, int fontSize = 13, float width = 0f, float height = 24f)
		{
			Image image = Panel_("Button", parent, background);
			Button button = image.gameObject.AddComponent<Button>();
			button.targetGraphic = image;
			ColorBlock colors = button.colors;
			colors.normalColor = Color.white;
			colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
			colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
			button.colors = colors;
			HorizontalLayoutGroup inner = image.gameObject.AddComponent<HorizontalLayoutGroup>();
			inner.padding = new RectOffset(10, 10, 3, 3);
			inner.childAlignment = TextAnchor.MiddleCenter;
			inner.childForceExpandWidth = false;
			inner.childForceExpandHeight = false;
			inner.childControlWidth = true;
			inner.childControlHeight = true;
			Label(image.transform, text, fontSize, textColor, TextAnchor.MiddleCenter);
			LayoutElement le = image.gameObject.AddComponent<LayoutElement>();
			le.preferredHeight = height;
			if (width > 0f)
			{
				le.preferredWidth = width;
			}

			HoldTracker tracker = image.gameObject.AddComponent<HoldTracker>();
			tracker.onPress = onPress;
			tracker.onRelease = onRelease;
		}

		/// <summary>Press/release tracker for HoldButton — see its doc. Left button only, matching Selectable's own press handling on the same object.</summary>
		private class HoldTracker : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
		{
			public UnityAction onPress;
			public UnityAction onRelease;
			private bool pressed;

			public void OnPointerDown(PointerEventData eventData)
			{
				if (eventData.button != PointerEventData.InputButton.Left || pressed)
				{
					return;
				}
				pressed = true;
				onPress?.Invoke();
			}

			public void OnPointerUp(PointerEventData eventData)
			{
				if (eventData.button == PointerEventData.InputButton.Left)
				{
					Release();
				}
			}

			private void OnDisable()
			{
				Release();
			}

			private void Release()
			{
				if (!pressed)
				{
					return;
				}
				pressed = false;
				onRelease?.Invoke();
			}
		}

		public static VerticalLayoutGroup Vertical(GameObject go, int padding, float spacing,
			bool childForceExpandWidth = true)
		{
			VerticalLayoutGroup group = go.AddComponent<VerticalLayoutGroup>();
			group.padding = new RectOffset(padding, padding, padding, padding);
			group.spacing = spacing;
			group.childForceExpandWidth = childForceExpandWidth;
			group.childForceExpandHeight = false;
			group.childControlWidth = true;
			group.childControlHeight = true;
			return group;
		}

		public static HorizontalLayoutGroup Horizontal(GameObject go, int padding, float spacing)
		{
			HorizontalLayoutGroup group = go.AddComponent<HorizontalLayoutGroup>();
			group.padding = new RectOffset(padding, padding, padding, padding);
			group.spacing = spacing;
			group.childForceExpandWidth = false;
			group.childForceExpandHeight = false;
			group.childControlWidth = true;
			group.childControlHeight = true;
			group.childAlignment = TextAnchor.MiddleLeft;
			return group;
		}

		public static LayoutElement Size(GameObject go, float width = -1f, float height = -1f, float flexibleWidth = -1f)
		{
			LayoutElement le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
			if (width >= 0f)
			{
				le.preferredWidth = width;
			}
			if (height >= 0f)
			{
				le.preferredHeight = height;
			}
			if (flexibleWidth >= 0f)
			{
				le.flexibleWidth = flexibleWidth;
			}
			return le;
		}

		public static GameObject Spacer(Transform parent, float flexible = 1f)
		{
			GameObject go = Go("Spacer", parent);
			LayoutElement le = go.AddComponent<LayoutElement>();
			le.flexibleWidth = flexible;
			return go;
		}

		/// <summary>Vertical scroll area of fixed height; returns the content transform to fill. Mouse wheel scrolls.</summary>
		public static RectTransform ScrollList(Transform parent, float height)
		{
			RectTransform outer = Bordered("Scroll", parent, Inset, Line);
			Size(outer.gameObject, -1f, height);

			GameObject viewportGo = Go("Viewport", outer);
			RectTransform viewport = (RectTransform)viewportGo.transform;
			Stretch(viewport, 1f);
			viewportGo.AddComponent<RectMask2D>();
			Image viewportImage = viewportGo.AddComponent<Image>();
			viewportImage.color = Color.clear;

			GameObject contentGo = Go("Content", viewport);
			RectTransform content = (RectTransform)contentGo.transform;
			content.anchorMin = new Vector2(0f, 1f);
			content.anchorMax = new Vector2(1f, 1f);
			content.pivot = new Vector2(0.5f, 1f);
			content.offsetMin = new Vector2(4f, 0f);
			content.offsetMax = new Vector2(-4f, 0f);
			Vertical(contentGo, 4, 3f);
			ContentSizeFitter fitter = contentGo.AddComponent<ContentSizeFitter>();
			fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			ScrollRect scroll = outer.gameObject.AddComponent<ScrollRect>();
			scroll.viewport = viewport;
			scroll.content = content;
			scroll.horizontal = false;
			scroll.vertical = true;
			scroll.movementType = ScrollRect.MovementType.Clamped;
			scroll.scrollSensitivity = 22f;
			return content;
		}

		/// <summary>Small single-line text field, inset style. Locks keyboard input away from scene shortcuts while focused.</summary>
		public static InputField Field(Transform parent, string text, float width,
			UnityAction<string> onEndEdit)
		{
			RectTransform rect = Bordered("Field", parent, Inset, Line);
			Size(rect.gameObject, width, 19f);
			Text textComponent = Label(rect, text, 12, Text, TextAnchor.MiddleCenter);
			Stretch(textComponent.rectTransform, 3f);
			textComponent.supportRichText = false;
			InputField field = rect.gameObject.AddComponent<InputField>();
			field.targetGraphic = rect.GetComponent<Image>();
			field.textComponent = textComponent;
			field.text = text;
			field.lineType = InputField.LineType.SingleLine;
			field.caretWidth = 1;
			field.selectionColor = new Color(Green.r, Green.g, Green.b, 0.35f);
			field.onEndEdit.AddListener(value =>
			{
				InputLockManager.RemoveControlLock(TypingLock.LockId);
				onEndEdit(value);
			});
			rect.gameObject.AddComponent<TypingLock>();
			return field;
		}

		/// <summary>Tiny square icon button. Stick to glyphs from the core Arrows block (U+2190-21FF) or Dingbats — the dynamic OS font does not cover Supplemental Arrows-A.</summary>
		public static Button IconButton(Transform parent, string glyph, UnityAction onClick,
			Color textColor, float size = 20f)
		{
			Image image = Panel_("Icon", parent, Panel2);
			Button button = image.gameObject.AddComponent<Button>();
			button.targetGraphic = image;
			button.onClick.AddListener(onClick);
			int glyphSize = Mathf.Max(13, Mathf.RoundToInt(size * 0.8f));
			Text label = Label(image.transform, glyph, glyphSize, textColor, TextAnchor.MiddleCenter);
			Stretch(label.rectTransform);
			LayoutElement le = image.gameObject.AddComponent<LayoutElement>();
			le.preferredWidth = size;
			le.preferredHeight = size;
			return button;
		}

		private class TypingLock : MonoBehaviour, UnityEngine.EventSystems.ISelectHandler,
			UnityEngine.EventSystems.IDeselectHandler
		{
			public const string LockId = "KRILL_EDITOR_TYPING";

			public void OnSelect(UnityEngine.EventSystems.BaseEventData eventData)
			{
				InputLockManager.SetControlLock(ControlTypes.KEYBOARDINPUT, LockId);
			}

			public void OnDeselect(UnityEngine.EventSystems.BaseEventData eventData)
			{
				InputLockManager.RemoveControlLock(LockId);
			}

			private void OnDisable()
			{
				InputLockManager.RemoveControlLock(LockId);
			}
		}
	}
}
