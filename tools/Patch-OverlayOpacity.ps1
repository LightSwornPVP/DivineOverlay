param(
    [Parameter(Mandatory = $true)]
    [string] $CecilPath,

    [Parameter(Mandatory = $true)]
    [string] $InputAssembly,

    [Parameter(Mandatory = $true)]
    [string] $OutputAssembly
)

$ErrorActionPreference = 'Stop'

Add-Type -Path (Resolve-Path -LiteralPath $CecilPath)

$inputPath = (Resolve-Path -LiteralPath $InputAssembly).Path
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($inputPath)
$module = $assembly.MainModule

$configType = $module.GetType('BlockOverlay.Models.Config.ModConfig')
$rendererBaseType = $module.GetType('BlockOverlay.Renderers.RendererBase')
$blockRendererType = $module.GetType('BlockOverlay.Renderers.BlockWireframeRenderer')
$entityRendererType = $module.GetType('BlockOverlay.Renderers.EntityWireframeRenderer')

if ($null -eq $configType -or $null -eq $rendererBaseType -or
    $null -eq $blockRendererType -or $null -eq $entityRendererType) {
    throw 'The input assembly does not have the expected Block Overlay types.'
}

if ($null -ne ($configType.Properties | Where-Object Name -eq 'OverlayOpacity')) {
    throw 'The input assembly already contains OverlayOpacity.'
}

$floatType = $module.TypeSystem.Single
$voidType = $module.TypeSystem.Void
$fieldAttributes = [Mono.Cecil.FieldAttributes]::Private
$methodAttributes = [Mono.Cecil.MethodAttributes] (
    [int] [Mono.Cecil.MethodAttributes]::Public -bor
    [int] [Mono.Cecil.MethodAttributes]::HideBySig -bor
    [int] [Mono.Cecil.MethodAttributes]::SpecialName
)

$opacityField = New-Object Mono.Cecil.FieldDefinition(
    '<OverlayOpacity>k__BackingField',
    $fieldAttributes,
    $floatType
)
$configType.Fields.Add($opacityField)

$clampMethod = $module.GetMemberReferences() |
    Where-Object FullName -eq 'System.Double System.Math::Clamp(System.Double,System.Double,System.Double)' |
    Select-Object -First 1
if ($null -eq $clampMethod) {
    throw 'Could not find System.Math.Clamp in the input assembly metadata.'
}

$getOpacity = New-Object Mono.Cecil.MethodDefinition(
    'get_OverlayOpacity',
    $methodAttributes,
    $floatType
)
$getIl = $getOpacity.Body.GetILProcessor()
$getIl.Append($getIl.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
$getIl.Append($getIl.Create([Mono.Cecil.Cil.OpCodes]::Ldfld, $opacityField))
$getIl.Append($getIl.Create([Mono.Cecil.Cil.OpCodes]::Conv_R8))
$getIl.Append($getIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R8, [double] 0))
$getIl.Append($getIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R8, [double] 1))
$getIl.Append($getIl.Create([Mono.Cecil.Cil.OpCodes]::Call, $clampMethod))
$getIl.Append($getIl.Create([Mono.Cecil.Cil.OpCodes]::Conv_R4))
$getIl.Append($getIl.Create([Mono.Cecil.Cil.OpCodes]::Ret))
$getOpacity.Body.MaxStackSize = 3
$configType.Methods.Add($getOpacity)

$setOpacity = New-Object Mono.Cecil.MethodDefinition(
    'set_OverlayOpacity',
    $methodAttributes,
    $voidType
)
$setOpacity.Parameters.Add((New-Object Mono.Cecil.ParameterDefinition(
    'value',
    [Mono.Cecil.ParameterAttributes]::None,
    $floatType
)))
$setIl = $setOpacity.Body.GetILProcessor()
$setIl.Append($setIl.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
$setIl.Append($setIl.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_1))
$setIl.Append($setIl.Create([Mono.Cecil.Cil.OpCodes]::Conv_R8))
$setIl.Append($setIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R8, [double] 0))
$setIl.Append($setIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R8, [double] 1))
$setIl.Append($setIl.Create([Mono.Cecil.Cil.OpCodes]::Call, $clampMethod))
$setIl.Append($setIl.Create([Mono.Cecil.Cil.OpCodes]::Conv_R4))
$setIl.Append($setIl.Create([Mono.Cecil.Cil.OpCodes]::Stfld, $opacityField))
$setIl.Append($setIl.Create([Mono.Cecil.Cil.OpCodes]::Ret))
$setOpacity.Body.MaxStackSize = 4
$configType.Methods.Add($setOpacity)

$opacityProperty = New-Object Mono.Cecil.PropertyDefinition(
    'OverlayOpacity',
    [Mono.Cecil.PropertyAttributes]::None,
    $floatType
)
$opacityProperty.GetMethod = $getOpacity
$opacityProperty.SetMethod = $setOpacity
$configType.Properties.Add($opacityProperty)

$configConstructor = $configType.Methods |
    Where-Object { $_.Name -eq '.ctor' -and -not $_.HasParameters } |
    Select-Object -First 1
$ctorIl = $configConstructor.Body.GetILProcessor()
$firstInstruction = $configConstructor.Body.Instructions[0]
$ctorIl.InsertBefore($firstInstruction, $ctorIl.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
$ctorIl.InsertBefore($firstInstruction, $ctorIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R4, [single] 1))
$ctorIl.InsertBefore($firstInstruction, $ctorIl.Create([Mono.Cecil.Cil.OpCodes]::Stfld, $opacityField))

$configField = $rendererBaseType.Fields |
    Where-Object Name -eq '_config' |
    Select-Object -First 1
$vec4Constructor = $module.GetMemberReferences() |
    Where-Object FullName -eq 'System.Void Vintagestory.API.MathTools.Vec4f::.ctor(System.Single,System.Single,System.Single,System.Single)' |
    Select-Object -First 1

function Replace-WithOpacityColor {
    param(
        [Mono.Cecil.MethodDefinition] $Method,
        [Mono.Cecil.Cil.Instruction] $Target
    )

    $il = $Method.Body.GetILProcessor()
    $first = $il.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R4, [single] 1)
    $il.Replace($Target, $first)
    $cursor = $first
    foreach ($instruction in @(
        $il.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R4, [single] 1),
        $il.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R4, [single] 1),
        $il.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0),
        $il.Create([Mono.Cecil.Cil.OpCodes]::Ldfld, $configField),
        $il.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $getOpacity),
        $il.Create([Mono.Cecil.Cil.OpCodes]::Newobj, $vec4Constructor)
    )) {
        $il.InsertAfter($cursor, $instruction)
        $cursor = $instruction
    }
    $Method.Body.MaxStackSize = [Math]::Max($Method.Body.MaxStackSize, 16)
}

$blockRender = $blockRendererType.Methods |
    Where-Object Name -eq 'OnRenderFrame' |
    Select-Object -First 1
$blockWhite = $blockRender.Body.Instructions |
    Where-Object { $_.OpCode -eq [Mono.Cecil.Cil.OpCodes]::Ldsfld -and $_.Operand.FullName -eq 'Vintagestory.API.MathTools.Vec4f Vintagestory.API.MathTools.ColorUtil::WhiteArgbVec' } |
    Select-Object -First 1
Replace-WithOpacityColor -Method $blockRender -Target $blockWhite

$entityRenderBox = $entityRendererType.Methods |
    Where-Object Name -eq 'RenderWireframeBox' |
    Select-Object -First 1
$entityWhite = $entityRenderBox.Body.Instructions |
    Where-Object { $_.OpCode -eq [Mono.Cecil.Cil.OpCodes]::Ldsfld -and $_.Operand.FullName -eq 'Vintagestory.API.MathTools.Vec4f Vintagestory.API.MathTools.ColorUtil::WhiteArgbVec' } |
    Select-Object -First 1
Replace-WithOpacityColor -Method $entityRenderBox -Target $entityWhite

$renderLabel = $rendererBaseType.Methods |
    Where-Object Name -eq 'RenderLabel' |
    Select-Object -First 1
$labelTint = $renderLabel.Body.Instructions |
    Where-Object OpCode -eq ([Mono.Cecil.Cil.OpCodes]::Ldnull) |
    Select-Object -Last 1
Replace-WithOpacityColor -Method $renderLabel -Target $labelTint

# Mono.Cecil otherwise tries to resolve VintagestoryAPI while rewriting these
# private compile-time constants. Their values are already inlined in every IL
# use, so retaining the unused metadata fields is unnecessary.
$guiOverlayType = $module.GetType('BlockOverlay.Gui.GuiBlockOverlay')
foreach ($constantName in @('_screenspaceRenderStage', '_wireframeRenderStage')) {
    $constantField = $guiOverlayType.Fields |
        Where-Object Name -eq $constantName |
        Select-Object -First 1
    if ($null -ne $constantField) {
        [void] $guiOverlayType.Fields.Remove($constantField)
    }
}

$outputFullPath = [System.IO.Path]::GetFullPath($OutputAssembly)
$assembly.Write($outputFullPath)
$assembly.Dispose()

Write-Output "Patched assembly written to $outputFullPath"
