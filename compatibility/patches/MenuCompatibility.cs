using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class MenuCompatibility
{
    private static int Main(string[] args)
    {
        if (args.Length != 1)
            return 2;

        var parameters = new ReaderParameters { ReadWrite = true };
        using (var assembly = AssemblyDefinition.ReadAssembly(args[0], parameters))
        {
            var versionAttribute = assembly.CustomAttributes.Single(attribute =>
                attribute.AttributeType.FullName == "System.Reflection.AssemblyInformationalVersionAttribute");
            var version = ((string)versionAttribute.ConstructorArguments[0].Value).Split('+')[0];
            if (version != "1.4.10")
                return 2;

            var module = assembly.MainModule;
            if (module.Mvid != new Guid("af9d4791-4d02-4f9a-913c-f1b753d250e3"))
                return 2;

            var menu = module.GetType("BeatThemAll.MetaGame.MainMenuScreen");
            var entries = menu.Methods.Single(candidate => candidate.Name == "add_menu_entries");
            var entitlement = entries.Body.Instructions.Single(instruction =>
                instruction.OpCode == OpCodes.Call && instruction.Operand is MethodReference &&
                ((MethodReference)instruction.Operand).FullName ==
                "System.Boolean BeatThemAll.MetaGame.program::is_dlc_purchased()");
            var ownedBranch = entitlement.Next;
            var continueMenu = ownedBranch.Operand as Instruction;
            if (ownedBranch.OpCode != OpCodes.Brtrue_S || continueMenu == null ||
                continueMenu.OpCode != OpCodes.Call ||
                ((MethodReference)continueMenu.Operand).FullName !=
                "System.Boolean BeatThemAll.main::get_NoOnline()" ||
                !entries.Body.Instructions.SkipWhile(instruction => instruction != ownedBranch)
                    .TakeWhile(instruction => instruction != continueMenu)
                    .Any(instruction => instruction.OpCode == OpCodes.Ldstr &&
                        (string)instruction.Operand == "MAIN_MENU_DLC"))
                return 2;

            // Only skip the mobile storefront entry. The entitlement call still runs.
            var menuIl = entries.Body.GetILProcessor();
            menuIl.InsertAfter(ownedBranch, menuIl.Create(OpCodes.Br_S, continueMenu));

            var characterSelect = module.GetType("BeatThemAll.MetaGame.CharacterSelectionScreen");
            var confirm = characterSelect.Methods.Single(candidate => candidate.Name == "validate_select_menu_event");
            var openStore = confirm.Body.Instructions.Single(instruction =>
                instruction.OpCode == OpCodes.Newobj && instruction.Operand is MethodReference target &&
                target.FullName == "System.Void BeatThemAll.MetaGame.DlcPurchaseScreen::.ctor()");
            var rejectLocked = openStore.Next.Next;
            if (openStore.Previous.OpCode != OpCodes.Ldarg_0 ||
                openStore.Previous.Previous.OpCode != OpCodes.Brfalse_S ||
                openStore.Previous.Previous.Previous.Operand is not MethodReference isLocked ||
                isLocked.FullName != "System.Boolean BeatThemAll.MetaGame.MetaGameConfigData/PlayableCharacter::get_IsDlcLocked()" ||
                openStore.Next.OpCode != OpCodes.Call ||
                openStore.Next.Operand is not MethodReference addScreen ||
                addScreen.FullName != "System.Void BeatThemAll.MetaGame.GameScreen::add_screen(BeatThemAll.MetaGame.GameScreen)" ||
                rejectLocked.OpCode != OpCodes.Ldc_I4_0 || rejectLocked.Next.OpCode != OpCodes.Ret ||
                openStore.Previous.Previous.Operand != rejectLocked.Next.Next)
                return 2;

            // Preserve the lock check and its false return while skipping the mobile store.
            var confirmIl = confirm.Body.GetILProcessor();
            confirmIl.Replace(openStore.Previous, confirmIl.Create(OpCodes.Nop));
            confirmIl.Replace(openStore.Next, confirmIl.Create(OpCodes.Nop));
            confirmIl.Replace(openStore, confirmIl.Create(OpCodes.Nop));

            var utils = module.GetType("CommonLib.utils");
            var method = utils.Methods.Single(candidate => candidate.Name == "get_version_identifier");
            if (!method.HasBody || !method.IsStatic || method.Parameters.Count != 2 ||
                method.Parameters.Any(parameter => parameter.ParameterType.MetadataType != MetadataType.String) ||
                method.ReturnType.MetadataType != MetadataType.String)
                return 2;

            method.Body.ExceptionHandlers.Clear();
            method.Body.Variables.Clear();
            method.Body.Instructions.Clear();
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldstr, version);
            il.Emit(OpCodes.Ret);
            assembly.Write();
            Console.WriteLine("Version label patched: " + version);
            Console.WriteLine("Android storefront entry suppressed");
            Console.WriteLine("Locked DLC character confirmation remains blocked without opening the storefront");
            return 0;
        }
    }
}
