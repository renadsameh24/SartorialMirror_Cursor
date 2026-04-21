using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class GarmentSelectorUIRuntime : MonoBehaviour
{
    [Header("Wiring")]
    public SmplGarmentManager garmentManager;

    [Header("UI")]
    public Vector2 panelPadding = new(12, 12);
    public Vector2 buttonSize = new(220, 52);
    public float buttonSpacing = 10f;
    public int canvasSortOrder = 10;

    private Canvas canvas;
    private RectTransform panel;
    private readonly List<Button> buttons = new();

    void Start()
    {
        if (garmentManager == null)
            garmentManager = FindObjectOfType<SmplGarmentManager>(true);

        EnsureUI();
        Rebuild();
    }

    public void Rebuild()
    {
        ClearButtons();
        if (garmentManager == null || !garmentManager.HasCatalog) return;

        var font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        for (int i = 0; i < garmentManager.catalog.garments.Count; i++)
        {
            int idx = i;
            var entry = garmentManager.catalog.garments[i];

            var group = new GameObject($"GarmentGroup_{idx}", typeof(RectTransform), typeof(VerticalLayoutGroup));
            group.transform.SetParent(panel, false);
            var groupLayout = group.GetComponent<VerticalLayoutGroup>();
            groupLayout.padding = new RectOffset(0, 0, 0, 0);
            groupLayout.spacing = 6f;
            groupLayout.childAlignment = TextAnchor.MiddleLeft;
            groupLayout.childControlHeight = false;
            groupLayout.childControlWidth = false;
            groupLayout.childForceExpandHeight = false;
            groupLayout.childForceExpandWidth = false;

            var bgo = new GameObject($"GarmentButton_{idx}", typeof(RectTransform), typeof(Image), typeof(Button));
            bgo.transform.SetParent(group.transform, false);

            var rt = (RectTransform)bgo.transform;
            rt.sizeDelta = buttonSize;

            var img = bgo.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.55f);

            var btn = bgo.GetComponent<Button>();
            btn.onClick.AddListener(() => garmentManager.TrySetActive(idx));

            var tgo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            tgo.transform.SetParent(bgo.transform, false);
            var trt = (RectTransform)tgo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(12, 8);
            trt.offsetMax = new Vector2(-12, -8);

            var text = tgo.GetComponent<Text>();
            text.font = font;
            text.fontSize = 18;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.text = string.IsNullOrWhiteSpace(entry?.displayName) ? $"Garment {idx + 1}" : entry.displayName;

            buttons.Add(btn);

            if (entry != null && entry.colorVariants != null && entry.colorVariants.Count > 1)
            {
                var row = new GameObject("ColorRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                row.transform.SetParent(group.transform, false);
                var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
                rowLayout.spacing = 8f;
                rowLayout.childAlignment = TextAnchor.MiddleLeft;
                rowLayout.childControlHeight = false;
                rowLayout.childControlWidth = false;
                rowLayout.childForceExpandHeight = false;
                rowLayout.childForceExpandWidth = false;

                for (int v = 0; v < entry.colorVariants.Count; v++)
                {
                    int variantIdx = v;
                    var cgo = new GameObject($"Color_{variantIdx}", typeof(RectTransform), typeof(Image), typeof(Button));
                    cgo.transform.SetParent(row.transform, false);
                    var crt = (RectTransform)cgo.transform;
                    crt.sizeDelta = new Vector2(28, 28);

                    var cimg = cgo.GetComponent<Image>();
                    cimg.color = entry.colorVariants[variantIdx];

                    var cbtn = cgo.GetComponent<Button>();
                    cbtn.onClick.AddListener(() =>
                    {
                        garmentManager.TrySetActive(idx);
                        garmentManager.TrySetColorVariant(variantIdx);
                    });

                    buttons.Add(cbtn);
                }
            }
        }
    }

    void EnsureUI()
    {
        EnsureEventSystem();

        canvas = GetComponentInChildren<Canvas>(true);
        if (canvas == null)
        {
            var cgo = new GameObject("GarmentUICanvas");
            cgo.transform.SetParent(transform, false);
            canvas = cgo.AddComponent<Canvas>();
            cgo.AddComponent<CanvasScaler>();
            cgo.AddComponent<GraphicRaycaster>();
        }

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = canvasSortOrder;

        if (panel == null)
        {
            var pgo = new GameObject("GarmentPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            pgo.transform.SetParent(canvas.transform, false);
            panel = (RectTransform)pgo.transform;

            panel.anchorMin = new Vector2(0, 0.5f);
            panel.anchorMax = new Vector2(0, 0.5f);
            panel.pivot = new Vector2(0, 0.5f);
            panel.anchoredPosition = new Vector2(panelPadding.x, 0);

            var bg = pgo.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.25f);
            bg.raycastTarget = false;

            var layout = pgo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset((int)panelPadding.x, (int)panelPadding.x, (int)panelPadding.y, (int)panelPadding.y);
            layout.spacing = buttonSpacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = false;
            layout.childControlWidth = false;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;

            var fitter = pgo.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
    }

    static void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>(true) != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    void ClearButtons()
    {
        foreach (var b in buttons)
        {
            if (b != null) Destroy(b.gameObject);
        }
        buttons.Clear();
    }
}

