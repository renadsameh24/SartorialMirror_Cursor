using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "SartorialMirror/Garment Catalog", fileName = "GarmentCatalog")]
public sealed class GarmentCatalog : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        public string displayName;
        public GameObject garmentPrefab;
        public Sprite thumbnail;

        [Header("Variants")]
        [Tooltip("Optional colorways. Applied as a tint to all garment materials that expose _BaseColor or _Color.")]
        public List<Color> colorVariants = new() { Color.white };

        [Min(0)]
        public int defaultColorVariantIndex = 0;
    }

    public List<Entry> garments = new();
}

