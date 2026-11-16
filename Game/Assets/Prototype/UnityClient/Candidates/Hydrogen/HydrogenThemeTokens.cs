using UnityEngine;

namespace Prototype.UnityClient.Candidates.Hydrogen
{
    /// <summary>Candidate B's obsidian / star-brass / arcane-cyan design tokens.</summary>
    internal static class HydrogenThemeTokens
    {
        internal static readonly Color Abyss = Hex(0x0B0F17);
        internal static readonly Color Panel = Hex(0x121824);
        internal static readonly Color Elevated = Hex(0x1A2232);
        internal static readonly Color Input = Hex(0x0E131D);
        internal static readonly Color Border = Hex(0x263147);
        internal static readonly Color Ink = Hex(0xF1EFE9);
        internal static readonly Color Secondary = Hex(0x9CA8BE);
        internal static readonly Color Muted = Hex(0x64728C);
        internal static readonly Color Gold = Hex(0xEAB308);
        internal static readonly Color Cyan = Hex(0x38BDF8);
        internal static readonly Color Emerald = Hex(0x22C55E);
        internal static readonly Color Crimson = Hex(0xF43F5E);
        internal static readonly Color Amber = Hex(0xF97316);
        internal static readonly Color Violet = Hex(0xA855F7);

        private static Color Hex(uint value)
        {
            return new Color32((byte)(value >> 16), (byte)(value >> 8), (byte)value, 255);
        }
    }
}
