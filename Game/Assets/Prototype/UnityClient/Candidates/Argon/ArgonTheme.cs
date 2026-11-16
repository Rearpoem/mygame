using UnityEngine;

namespace Prototype.UnityClient.Candidates.Argon
{
    // Tokens transcribed from the real Argon DESIGN.md, section 3.1.
    internal static class ArgonTheme
    {
        internal static readonly Color Canvas = Hex(0x0B0E14);
        internal static readonly Color Panel = Hex(0x131822);
        internal static readonly Color Card = Hex(0x1C2433);
        internal static readonly Color Selected = Hex(0x263248);
        internal static readonly Color Well = Hex(0x0E131B);
        internal static readonly Color Border = Hex(0x2A354B);
        internal static readonly Color Brass = Hex(0xD4A359);
        internal static readonly Color Focus = Hex(0x38BDF8);
        internal static readonly Color Ink = Hex(0xF1F5F9);
        internal static readonly Color Secondary = Hex(0xCBD5E1);
        internal static readonly Color Muted = Hex(0x7E8FA6);
        internal static readonly Color Disabled = Hex(0x475569);
        internal static readonly Color Actor = Hex(0xF59E0B);
        internal static readonly Color Mp = Hex(0x0EA5E9);
        internal static readonly Color Wait = Hex(0x2DD4BF);
        internal static readonly Color Ally = Hex(0x10B981);
        internal static readonly Color Warning = Hex(0xFBBF24);
        internal static readonly Color Enemy = Hex(0xEF4444);
        internal static readonly Color Cover = Hex(0xFB7185);
        internal static readonly Color Debuff = Hex(0xA855F7);
        internal static readonly Color Buff = Hex(0x3B82F6);

        private static Color Hex(int rgb)
        {
            return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
        }
    }
}
