using System.IO.Compression;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.FreeSql;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Sb.Apps.Pm.Services.ImageProcessing
{
    /// <summary>
    /// 图片处理服务：多分辨率生成 / 格式转换 / 生成 favicon.ico / 缩放裁剪背景。
    /// 纯处理服务，不建表；实现 IModuleInitializer 仅用于注册菜单入口。
    /// </summary>
    [AutoDependencyInjection]
    public class ImageProcessingService : IModuleInitializer
    {
        #region IModuleInitializer

        public string ModuleName => "图片处理工具";
        public int InitOrder => 200;
        // 纯处理，无实体表
        public Type[] GetEntityTypes() => Array.Empty<Type>();
        public List<MenuDefinition> GetMenus() => new()
        {
            new MenuDefinition
            {
                ParentPath = "/ops-tools",
                Name = "ImageTool",
                Path = "/ops-tools/image-tool",
                Component = "/image-tool/index",
                Title = "图片处理",
                Icon = "lucide:image",
                OrderNo = 30
            }
        };

        #endregion

        private readonly ILogger<ImageProcessingService> _logger;

        public ImageProcessingService(ILogger<ImageProcessingService> logger)
        {
            _logger = logger;
        }

        /// <summary>支持的输出格式</summary>
        public static readonly string[] SupportedFormats = { "png", "jpg", "jpeg", "webp", "bmp", "gif" };

        /// <summary>favicon 默认尺寸集</summary>
        public static readonly int[] DefaultFaviconSizes = { 16, 32, 48, 64, 128, 256 };

        /// <summary>多分辨率默认尺寸集</summary>
        public static readonly int[] DefaultResolutions = { 16, 32, 64, 128, 256, 512 };

        // ── 1. 多分辨率生成（打包 zip）────────────────────────────────
        /// <summary>
        /// 一张图生成多个尺寸，打包成 zip 返回字节流。
        /// </summary>
        public async Task<byte[]> GenerateMultiResolutionZipAsync(
            Stream input, int[] sizes, string format = "png", int quality = 90,
            string fit = "contain", string? background = null)
        {
            sizes = (sizes is { Length: > 0 } ? sizes : DefaultResolutions)
                .Where(s => s is > 0 and <= 4096).Distinct().OrderBy(s => s).ToArray();
            if (sizes.Length == 0) sizes = DefaultResolutions;

            var ext = NormalizeFormat(format);
            using var srcImage = await Image.LoadAsync<Rgba32>(input);

            using var zipStream = new MemoryStream();
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var size in sizes)
                {
                    using var clone = srcImage.Clone(ctx => ResizeTo(ctx, size, size, fit, background));
                    var entry = zip.CreateEntry($"icon-{size}x{size}.{ext}", CompressionLevel.Optimal);
                    await using var es = entry.Open();
                    await EncodeAsync(clone, es, ext, quality);
                }
            }
            return zipStream.ToArray();
        }

        // ── 2. 格式转换 ────────────────────────────────────────────
        /// <summary>
        /// 转换图片格式，可选同时缩放。返回 (字节流, contentType, 文件扩展名)。
        /// </summary>
        public async Task<(byte[] bytes, string contentType, string ext)> ConvertFormatAsync(
            Stream input, string format, int quality = 90,
            int? width = null, int? height = null, string fit = "contain", string? background = null)
        {
            var ext = NormalizeFormat(format);
            using var image = await Image.LoadAsync<Rgba32>(input);

            if (width is > 0 || height is > 0)
                image.Mutate(ctx => ResizeTo(ctx, width ?? 0, height ?? 0, fit, background));

            using var ms = new MemoryStream();
            await EncodeAsync(image, ms, ext, quality);
            return (ms.ToArray(), GetContentType(ext), ext);
        }

        // ── 3. 生成 favicon.ico（多尺寸合一）─────────────────────────
        /// <summary>
        /// 从一张图生成多尺寸 .ico。手写 ICO 容器，内嵌多个 PNG 图像。
        /// </summary>
        public async Task<byte[]> GenerateFaviconAsync(Stream input, int[]? sizes = null)
        {
            sizes = (sizes is { Length: > 0 } ? sizes : DefaultFaviconSizes)
                .Where(s => s is > 0 and <= 256).Distinct().OrderBy(s => s).ToArray();
            if (sizes.Length == 0) sizes = DefaultFaviconSizes;

            using var srcImage = await Image.LoadAsync<Rgba32>(input);

            // 为每个尺寸生成 PNG 字节
            var pngs = new List<byte[]>();
            foreach (var size in sizes)
            {
                using var clone = srcImage.Clone(ctx => ResizeTo(ctx, size, size, "contain", null));
                using var ms = new MemoryStream();
                await clone.SaveAsPngAsync(ms);
                pngs.Add(ms.ToArray());
            }

            return BuildIco(sizes, pngs);
        }

        // ── 4. 缩放 / 裁剪 / 背景（单图处理）──────────────────────────
        /// <summary>
        /// 缩放/裁剪到指定宽高，返回 (字节流, contentType, ext)。
        /// </summary>
        public async Task<(byte[] bytes, string contentType, string ext)> ResizeAsync(
            Stream input, int width, int height, string fit = "contain",
            string? background = null, string format = "png", int quality = 90)
        {
            var ext = NormalizeFormat(format);
            using var image = await Image.LoadAsync<Rgba32>(input);
            image.Mutate(ctx => ResizeTo(ctx, width, height, fit, background));

            using var ms = new MemoryStream();
            await EncodeAsync(image, ms, ext, quality);
            return (ms.ToArray(), GetContentType(ext), ext);
        }

        // ── 私有：缩放逻辑 ──────────────────────────────────────────
        private static void ResizeTo(IImageProcessingContext ctx, int width, int height, string fit, string? background)
        {
            // 只给一个维度时按等比；fit: contain(留白) / cover(裁剪填满) / stretch(拉伸)
            var w = width > 0 ? width : 0;
            var h = height > 0 ? height : 0;
            if (w == 0 && h == 0) return;

            var mode = fit?.ToLowerInvariant() switch
            {
                "cover" => ResizeMode.Crop,
                "stretch" => ResizeMode.Stretch,
                _ => ResizeMode.Pad, // contain：留白填充
            };

            var options = new ResizeOptions
            {
                Size = new Size(w == 0 ? h : w, h == 0 ? w : h),
                Mode = mode,
                Position = AnchorPositionMode.Center,
            };

            // contain 模式下背景色（默认透明）
            if (mode == ResizeMode.Pad && !string.IsNullOrWhiteSpace(background))
            {
                options.PadColor = Color.Parse(background);
            }

            ctx.Resize(options);

            // contain 且指定了不透明背景时，把透明区域填实
            if (mode == ResizeMode.Pad && !string.IsNullOrWhiteSpace(background))
            {
                ctx.BackgroundColor(Color.Parse(background));
            }
        }

        // ── 私有：编码到指定格式 ─────────────────────────────────────
        private static async Task EncodeAsync(Image image, Stream output, string ext, int quality)
        {
            quality = Math.Clamp(quality, 1, 100);
            switch (ext)
            {
                case "jpg":
                case "jpeg":
                    await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = quality });
                    break;
                case "webp":
                    await image.SaveAsWebpAsync(output, new WebpEncoder { Quality = quality });
                    break;
                case "bmp":
                    await image.SaveAsBmpAsync(output, new BmpEncoder());
                    break;
                case "gif":
                    await image.SaveAsGifAsync(output, new GifEncoder());
                    break;
                default: // png
                    await image.SaveAsPngAsync(output, new PngEncoder());
                    break;
            }
        }

        // ── 私有：手写 ICO 容器（内嵌 PNG）────────────────────────────
        private static byte[] BuildIco(int[] sizes, List<byte[]> pngs)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            int count = pngs.Count;
            // ICONDIR
            bw.Write((ushort)0);      // reserved
            bw.Write((ushort)1);      // type = icon
            bw.Write((ushort)count);  // image count

            // 目录项之后是图像数据，先算偏移
            int offset = 6 + 16 * count;
            for (int i = 0; i < count; i++)
            {
                int size = sizes[i];
                byte dim = (byte)(size >= 256 ? 0 : size); // 256 用 0 表示
                // ICONDIRENTRY
                bw.Write(dim);            // width
                bw.Write(dim);            // height
                bw.Write((byte)0);        // palette
                bw.Write((byte)0);        // reserved
                bw.Write((ushort)1);      // color planes
                bw.Write((ushort)32);     // bits per pixel
                bw.Write((uint)pngs[i].Length); // data size
                bw.Write((uint)offset);   // data offset
                offset += pngs[i].Length;
            }
            // 图像数据
            foreach (var png in pngs) bw.Write(png);

            bw.Flush();
            return ms.ToArray();
        }

        // ── 私有：工具 ─────────────────────────────────────────────
        private static string NormalizeFormat(string? format)
        {
            var f = (format ?? "png").Trim().ToLowerInvariant().TrimStart('.');
            return SupportedFormats.Contains(f) ? f : "png";
        }

        public static string GetContentType(string ext) => ext switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "webp" => "image/webp",
            "bmp" => "image/bmp",
            "gif" => "image/gif",
            "ico" => "image/x-icon",
            _ => "image/png",
        };
    }
}
