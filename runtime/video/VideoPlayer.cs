// SPDX-License-Identifier: MIT
using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Media;

namespace Sorage4;

public sealed class VideoPlayer : IDisposable
{
    private const int Width = 640, Height = 360, Rate = 30;
    private const int FrameSize = Width * Height * 4;
    private readonly Texture2D texture;
    private readonly CancellationTokenSource stop = new();
    private readonly Stopwatch clock = new();
    private readonly Channel<byte[]> frames = Channel.CreateBounded<byte[]>(3);
    private Task videoTask = Task.CompletedTask;
    private bool finished, started, paused;
    private int frame = -1;

    public VideoPlayer(GraphicsDevice device)
    {
        texture = new Texture2D(device, Width, Height);
        texture.SetData(new byte[FrameSize]);
    }

    private static string? ClipPath(string filename)
    {
        string? assets = Environment.GetEnvironmentVariable("SOR4_ASSETS");
        string name = Path.GetFileName(filename);
        return assets == null || !name.EndsWith(".mp4", StringComparison.Ordinal)
            ? null : Path.Combine(assets, "videos", name);
    }

    public static bool Exists(string name) => File.Exists(ClipPath(name + ".mp4"));

    public void Play(string filename)
    {
        string? path = ClipPath(filename);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine("[video] Missing clip: " + filename);
            finished = true;
            return;
        }
        Console.Error.WriteLine("[video] Playing " + filename);
        videoTask = Task.Run(() => DecodeVideo(path!));
    }

    private async Task Decode(string path, string[] output, Func<Stream, Task> consume)
    {
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "..", "utils", "sorage-video"))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };
        foreach (var arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "error",
                                   "-threads", "2", "-filter_threads", "1", "-i", path }.Concat(output))
            info.ArgumentList.Add(arg);
        using var child = Process.Start(info) ?? throw new IOException("Could not start video decoder.");
        using var kill = stop.Token.Register(() =>
        {
            try { if (!child.HasExited) child.Kill(); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
        });
        try
        {
            await consume(child.StandardOutput.BaseStream);
            await child.WaitForExitAsync();
            if (child.ExitCode != 0 && !stop.IsCancellationRequested)
                throw new IOException("Decoder exited with status " + child.ExitCode);
        }
        finally
        {
            if (!child.HasExited)
                child.Kill();
            await child.WaitForExitAsync();
        }
    }

    private async Task DecodeVideo(string path)
    {
        try
        {
            await Decode(path, new[] { "-map", "0:v:0", "-an", "-vf", "fps=30,scale=640:360",
                                       "-pix_fmt", "rgba", "-c:v", "rawvideo", "-threads", "1",
                                       "-f", "rawvideo", "pipe:1" }, async stream =>
            {
                while (await frames.Writer.WaitToWriteAsync(stop.Token))
                {
                    byte[] pixels = ArrayPool<byte>.Shared.Rent(FrameSize);
                    try
                    {
                        await stream.ReadExactlyAsync(pixels.AsMemory(0, FrameSize), stop.Token);
                        if (!frames.Writer.TryWrite(pixels))
                            break;
                        pixels = null!;
                    }
                    catch (EndOfStreamException) { break; }
                    finally { if (pixels != null) ArrayPool<byte>.Shared.Return(pixels); }
                }
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!stop.IsCancellationRequested) Console.Error.WriteLine("[video] " + e.Message); }
        finally { frames.Writer.TryComplete(); }
    }

    public MediaState State => finished ? MediaState.Stopped : paused ? MediaState.Paused : MediaState.Playing;
    public TimeSpan PlayPosition => finished && !started ? TimeSpan.FromTicks(1) : clock.Elapsed;
    // The game also checks State to detect EOF. Keep duration and position at zero until playback starts.
    public TimeSpan Duration => finished ? PlayPosition : !started ? TimeSpan.Zero : TimeSpan.MaxValue;

    public void Pause()
    {
        paused = true;
        clock.Stop();
    }

    public void Resume()
    {
        paused = false;
        if (!started || finished)
            return;
        clock.Start();
    }

    public Texture2D GetTexture()
    {
        if (finished || paused)
            return texture;
        if (!started)
        {
            if (!frames.Reader.TryPeek(out _))
            {
                if (frames.Reader.Completion.IsCompleted)
                    finished = true;
                return texture;
            }
            clock.Start();
            started = true;
        }

        long target = (long)(clock.Elapsed.TotalSeconds * Rate);
        byte[]? latest = null;
        while (frame < target && frames.Reader.TryRead(out var next))
        {
            if (latest != null)
                ArrayPool<byte>.Shared.Return(latest);
            latest = next;
            frame++;
        }
        if (latest != null)
        {
            try { texture.SetData(latest, 0, FrameSize); }
            finally { ArrayPool<byte>.Shared.Return(latest); }
        }
        if (frames.Reader.Completion.IsCompleted && target > frame)
        {
            finished = true;
            Console.Error.WriteLine("[video] Finished after " + (frame + 1) + " frames");
        }
        return texture;
    }

    public void Dispose()
    {
        stop.Cancel();
        videoTask.GetAwaiter().GetResult();
        while (frames.Reader.TryRead(out var pixels))
            ArrayPool<byte>.Shared.Return(pixels);
        texture.Dispose();
        stop.Dispose();
    }
}
