// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class EnableVideo
{
    public static void Main(string[] args)
    {
        using var player = AssemblyDefinition.ReadAssembly(Path.Combine(AppContext.BaseDirectory, "SorageVideo.dll"));
        // The apphost runs this tool from runtime.aarch64 while preparing the pinned APK.
        using var game = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { ReadWrite = true });
        var module = game.MainModule;
        var type = player.MainModule.GetType("Sorage4.VideoPlayer");
        var platform = module.GetType("CommonLib.platform");
        var field = platform.Fields.Single(f => f.Name == "videoPlayer");
        if (field.FieldType.FullName != "Microsoft.Xna.Framework.Media.SuperVideoPlayer")
            throw new InvalidDataException("Unexpected game video player field");
        var start = platform.Methods.Single(m => m.Name == "video_start");
        var getDevice = (MethodReference)start.Body.Instructions.Single(i =>
            i.Operand is MethodReference m && m.FullName.EndsWith("CommonLib.graphics::get_Device()")).Operand;
        var exists = platform.Methods.Single(m => m.Name == "video_exists");
        exists.Body = new MethodBody(exists);
        var il = exists.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, module.ImportReference(type.Methods.Single(m => m.Name == "Exists")));
        il.Emit(OpCodes.Ret);

        field.FieldType = module.ImportReference(type);
        int replacements = 0;
        foreach (var method in platform.Methods.Where(m => m.HasBody))
        {
            foreach (var instruction in method.Body.Instructions.ToArray())
            {
                if (instruction.Operand is not MethodReference old ||
                    old.DeclaringType.FullName != "Microsoft.Xna.Framework.Media.SuperVideoPlayer")
                    continue;
                var replacement = type.Methods.Single(m => m.Name == old.Name);
                if (old.Name == ".ctor")
                {
                    // Keep the original instruction as the target of any existing branches.
                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = getDevice;
                    method.Body.GetILProcessor().InsertAfter(instruction,
                        Instruction.Create(OpCodes.Newobj, module.ImportReference(replacement)));
                }
                else
                    instruction.Operand = module.ImportReference(replacement);
                replacements++;
            }
        }
        if (replacements != 14)
            throw new InvalidDataException("Unexpected number of game video calls: " + replacements);
        // Inserting a call into video_start can move an existing short branch.
        foreach (var instruction in start.Body.Instructions)
            if (instruction.OpCode == OpCodes.Brfalse_S)
                instruction.OpCode = OpCodes.Brfalse;
        game.Write();
        Console.WriteLine("Linux video player connected");
    }
}
