using System.Runtime.InteropServices;

namespace SpaceSnoop.Wpf.Bootstrap.Platform;

internal static class DirectWriteFontPin
{
    private const int DWriteFactoryTypeShared = 0;

    private static readonly string[] DefaultFamilies =
    [
        "Segoe UI",
        "Segoe UI Variable",
        "Segoe UI Variable Text",
        "Segoe UI Variable Display",
        "Segoe UI Variable Small",
        "Cascadia Mono",
        "Consolas",
        "Georgia",
        "Cambria",
        "Times New Roman",
        "Bahnschrift",
        "Courier New",
        "Segoe MDL2 Assets",
        "Segoe Fluent Icons",
        "Selawik",
        "Inter",
        "DIN Condensed",
        "Roboto Mono",
    ];

    private static readonly Lock Gate = new();
    private static readonly List<object> Pinned = [];
    private static bool _initialized;

    internal static int ProbedFaceCount { get; private set; }

    internal static int PinAttempts { get; private set; }

    public static int PinSystemFonts()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return Pinned.Count;
            }

            _initialized = true;
            PinAttempts++;

            try
            {
                var factoryGuid = typeof(IDWriteFactory).GUID;

                if (DWriteCreateFactory(DWriteFactoryTypeShared, in factoryGuid, out var factoryObj) != 0
                    || factoryObj is not IDWriteFactory factory)
                {
                    return 0;
                }

                Pinned.Add(factory);

                if (factory.GetSystemFontCollection(out var collection, false) != 0 || collection is null)
                {
                    return 0;
                }

                Pinned.Add(collection);
                uint[] codePoints = ['0', 'A', 'А'];
                var glyphIndices = new ushort[codePoints.Length];

                foreach (var familyName in DefaultFamilies)
                {
                    if (collection.FindFamilyName(familyName, out var index, out var exists) != 0 || !exists)
                    {
                        continue;
                    }

                    if (collection.GetFontFamily(index, out var family) != 0 || family is null)
                    {
                        continue;
                    }

                    Pinned.Add(family);
                    var count = family.GetFontCount();

                    for (uint i = 0; i < count; i++)
                    {
                        if (family.GetFont(i, out var font) != 0 || font is null)
                        {
                            continue;
                        }

                        if (font.CreateFontFace(out var face) == 0
                            && face is not null
                            && face.GetGlyphIndices(codePoints, (uint)codePoints.Length, glyphIndices) == 0)
                        {
                            ProbedFaceCount++;
                            Pinned.Add(font);
                            Pinned.Add(face);
                        }
                    }
                }

                return Pinned.Count;
            }
            catch
            {
                return Pinned.Count;
            }
        }
    }

    [DllImport("dwrite.dll", ExactSpelling = true)]
    private static extern int DWriteCreateFactory(
        int factoryType,
        in Guid iid,
        [MarshalAs(UnmanagedType.IUnknown)] out object factory);

    [ComImport]
    [Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDWriteFactory
    {
        [PreserveSig]
        int GetSystemFontCollection(
            out IDWriteFontCollection? fontCollection,
            [MarshalAs(UnmanagedType.Bool)] bool checkForUpdates);
    }

    [ComImport]
    [Guid("a84cee02-3eea-4eee-a827-87c1a02a0fcc")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDWriteFontCollection
    {
        [PreserveSig]
        uint GetFontFamilyCount();

        [PreserveSig]
        int GetFontFamily(uint index, out IDWriteFontFamily? fontFamily);

        [PreserveSig]
        int FindFamilyName(
            [MarshalAs(UnmanagedType.LPWStr)] string familyName,
            out uint index,
            [MarshalAs(UnmanagedType.Bool)] out bool exists);
    }

    [ComImport]
    [Guid("da20d8ef-812a-4c43-9802-62ec4abd7add")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDWriteFontFamily
    {
        [PreserveSig]
        int GetFontCollection(out IDWriteFontCollection? fontCollection);

        [PreserveSig]
        uint GetFontCount();

        [PreserveSig]
        int GetFont(uint index, out IDWriteFont? font);
    }

    [ComImport]
    [Guid("acd16696-8c14-4f5d-877e-fe3fc1d32737")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDWriteFont
    {
        [PreserveSig]
        int GetFontFamily(out IDWriteFontFamily? fontFamily);

        [PreserveSig]
        int GetWeight();

        [PreserveSig]
        int GetStretch();

        [PreserveSig]
        int GetStyle();

        [PreserveSig]
        [return: MarshalAs(UnmanagedType.Bool)]
        bool IsSymbolFont();

        [PreserveSig]
        int GetFaceNames(out nint names);

        [PreserveSig]
        int GetInformationalStrings(int informationalStringId, out nint informationalStrings, [MarshalAs(UnmanagedType.Bool)] out bool exists);

        [PreserveSig]
        int GetSimulations();

        [PreserveSig]
        void GetMetrics(nint fontMetrics);

        [PreserveSig]
        int HasCharacter(uint unicodeValue, [MarshalAs(UnmanagedType.Bool)] out bool exists);

        [PreserveSig]
        int CreateFontFace(out IDWriteFontFace? fontFace);
    }

    [ComImport]
    [Guid("5f49804d-7024-4d43-bfa9-d25984f53849")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDWriteFontFace
    {
        [PreserveSig]
        int GetFontFaceType();

        [PreserveSig]
        int GetFiles(ref uint numberOfFiles, nint fontFiles);

        [PreserveSig]
        uint GetIndex();

        [PreserveSig]
        int GetSimulations();

        [PreserveSig]
        [return: MarshalAs(UnmanagedType.Bool)]
        bool IsSymbolFont();

        [PreserveSig]
        void GetMetrics(nint fontFaceMetrics);

        [PreserveSig]
        ushort GetGlyphCount();

        [PreserveSig]
        int GetDesignGlyphMetrics(nint glyphIndices, uint glyphCount, nint glyphMetrics, [MarshalAs(UnmanagedType.Bool)] bool isSideways);

        [PreserveSig]
        int GetGlyphIndices(
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] uint[] codePoints,
            uint codePointCount,
            [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] ushort[] glyphIndices);
    }
}
