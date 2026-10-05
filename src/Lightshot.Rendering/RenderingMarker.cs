using SkiaSharp;
using Lightshot.Core;

namespace Lightshot.Rendering;

public static class RenderingMarker
{
    public const string ProjectName = "Lightshot.Rendering";

    public static string GetCoreMarker() => CoreMarker.ProjectName;
}
