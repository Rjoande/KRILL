using UnityEngine;
using UnityEngine.UI;

namespace KRILL.UI
{
	/// <summary>
	/// First real (non-mockup) pass at the Apollo console — geometry only, no
	/// bevel/glow/severity/text yet (Categoria 1 "faccio io" work, see
	/// notes/console-art-pipeline.md). Renders the FLAT placeholder art the
	/// user measured by hand (dev/console_templates/apollo_background.png,
	/// apollo_AGbutton.png) purely to check that the grid proportions read
	/// correctly on an actual screen at real UI_SCALE — not the finished look.
	///
	/// WIP on the art/chrome side: no drag, no SET/PAGE/mode-select frame yet
	/// (not locked). Visibility IS wired up for real though (2026-08-30,
	/// right-click on the KRILL toolbar icon — see KrillToolbarApp), meant as
	/// a permanent shortcut going forward, not a throwaway test toggle.
	/// Starts hidden each time the flight scene loads (no persistence across
	/// scenes, matches this KSPAddon being recreated on every scene entry).
	/// Every Image has raycastTarget off so it never blocks flight input.
	///
	/// Preview builds only (KRILL_CONSOLE_PREVIEW, see KRILL.csproj, 2026-09-02):
	/// in a release build the KSPAddon attribute is compiled out, so this class
	/// is never instantiated, loads no texture and has no toolbar hook.
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

		// Measured by hand by the user against the Apollo layout template
		// (dev/console_templates/apollo_template_1.png), not derived here.
		private const float BackgroundWidth = 1300f;
		private const float BackgroundHeight = 900f;
		private const float ButtonWidth = 300f;
		private const float ButtonHeight = 90f;
		private const float GridMarginX = 36f; // left edge of the background to the first button's left edge
		private const float GridMarginY = 87f; // top edge of the background to the first button's top edge
		private const float GutterX = 7f;
		private const float GutterY = 5f; // tighter than GutterX by user's eye once seen in game (2026-08-30)
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
			// Centers the panel on screen: pivot sits at the panel's top-left
			// corner, so offsetting it half a size up-left from screen center
			// puts the panel's actual center on screen center.
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
