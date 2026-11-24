"""Create the GLES build project from the pinned MonoGame DesktopGL project."""

from pathlib import Path
import sys
from xml.etree import ElementTree as ET


framework = Path(sys.argv[1])
project = ET.parse(framework / "MonoGame.Framework.DesktopGL.csproj")
root = project.getroot()
properties = root.find("PropertyGroup")
properties.find("TargetFramework").text = "net9.0"
defines = properties.find("DefineConstants")
defines.text = defines.text.replace("STBSHARP_INTERNAL", "GLES")
properties.find("IsAotCompatible").text = "false"
properties.find("CopyContentFiles").text = "False"
for group in root.findall("ItemGroup"):
    for item in list(group):
        if item.tag == "PackageReference" or item.get("Include", "").startswith("..\\ThirdParty\\StbImage"):
            group.remove(item)

references = ET.SubElement(root, "ItemGroup")
for name in ("Mono.Android", "Java.Interop", "SORAGE4Bridge", "NVorbis",
             "StbImageSharp", "StbImageWriteSharp"):
    reference = ET.SubElement(references, "Reference", Include=name)
    ET.SubElement(reference, "HintPath").text = f"$(PortableRuntime)/{name}.dll"
ET.SubElement(references, "Compile", Include="Platform/SOR4Compat.cs")
ET.indent(project, space="  ")
project.write(framework / "MonoGame.Framework.SORAGE4.csproj", encoding="utf-8")
