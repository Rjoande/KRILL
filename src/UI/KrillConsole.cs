using UnityEngine;
using UnityEngine.UI;

namespace KRILL.UI
{
	/// <summary>
	/// WIP Apollo console: grid geometry only, on flat placeholder art. Starts
	/// hidden on every flight scene load, toggled by right-clicking the toolbar
	/// icon; every Image has raycastTarget off so it never blocks flight input.
	/// </summary>
#if KRILL_CONSOLE_PREVIEW
	[KSPAddon(KSPAddon.Startup.Flight, false)]
#endif
	public class KrillConsole : MonoBehaviour
	{
		private static KrillConsole current;

		/// <summary>Toolbar right-click callback. No-op outside flight (current is null there).</summary>
		public static void ToggleVisible()
		{
			if (current != null)
			{
				current.gameObject.SetActive(!current.gameObject.activeSelf);
			}
		}

		private const string BackgroundTexture = "KRILL/Textures/Console/Apollo/apollo_background";
		private const string ButtonTexture = "KRILL/Textures/Console/Apollo/apollo_AGbutton";

		// Measured by hand against the Apollo layout template, not derived here.
		private const float BackgroundWidth = 1300f;
		private const float BackgroundHeight = 900f;
		private const float ButtonWidth = 300f;
		private const float ButtonHeight = 90f;
		private const float GridMarginX = 36f; // left edge of the background to the first button's left edge
		private const float GridMarginY = 87f; // top edge of the background to the first button's top edge
		private const float GutterX = 7f;
		private const float GutterY = 5f; // deliberately tighter than GutterX
		private const int Columns = 4;
		private const int Rows = 6; // 4x6 = 24 cells, matches extended groups 11-34

		private void Start()
		{
			current = this;

			Canvas canvas = gameObject.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 850;
			CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
			scaler.scaleFactor = GameSettings.UI_SCALE;

			RectTransform background = BuildImage(transform, BackgroundTexture, BackgroundWidth, BackgroundHeight);
			background.pivot = new Vector2(0f, 1f);
			background.anchorMin = background.anchorMax = new Vector2(0.5f, 0.5f);
			// The pivot is the panel's top-left corner, so offsetting it half a size
			// up-left from screen center puts the panel's own center on screen center.
			background.anchoredPosition = new Vector2(-BackgroundWidth * 0.5f, BackgroundHeight * 0.5f);

			for (int row = 0; row < Rows; row++)
			{
				for (int col = 0; col < Columns; col++)
				{
					RectTransform button = BuildImage(background, ButtonTexture, ButtonWidth, ButtonHeight);
					button.pivot = new Vector2(0f, 1f);
					button.anchorMin = button.anchorMax = new Vector2(0f, 1f); // top-left of the background panel
					button.anchoredPosition = new Vector2(
						GridMarginX + col * (ButtonWidth + GutterX),
						-(GridMarginY + row * (ButtonHeight + GutterY)));
				}
			}

			gameObject.SetActive(false);
		}

		private void OnDestroy()
		{
			if (current == this)
			{
				current = null;
			}
		}

		private static RectTransform BuildImage(Transform parent, string texturePath, float width, float height)
		{
			Texture2D tex = GameDatabase.Instance.GetTexture(texturePath, false);
			GameObject go = new GameObject("KrillConsoleImg", typeof(RectTransform));
			go.transform.SetParent(parent, false);
			Image image = go.AddComponent<Image>();
			image.raycastTarget = false;
			if (tex != null)
			{
				image.sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
			}
			else
			{
				Debug.LogWarning($"[KRILL] KrillConsole: texture not found at GameDatabase path '{texturePath}'");
			}
			RectTransform rect = (RectTransform)go.transform;
			rect.sizeDelta = new Vector2(width, height);
			return rect;
		}
	}
}
