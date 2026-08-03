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

$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly(
    (Resolve-Path -LiteralPath $InputAssembly).Path
)
$module = $assembly.MainModule
$pickerType = $module.GetType('BlockOverlay.Gui.GuiColorPickerDialog')

if ($null -eq $pickerType) {
    throw 'The input assembly does not contain GuiColorPickerDialog.'
}
if ($null -ne ($pickerType.Fields | Where-Object Name -eq '_a')) {
    throw 'The input assembly already contains the color-picker opacity patch.'
}

function Find-MemberReference {
    param([string] $FullName)

    $reference = $module.GetMemberReferences() |
        Where-Object FullName -eq $FullName |
        Select-Object -First 1
    if ($null -eq $reference) {
        throw "Could not find metadata reference: $FullName"
    }
    return $reference
}

$intType = $module.TypeSystem.Int32
$voidType = $module.TypeSystem.Void
$stringType = $module.TypeSystem.String
$opacityField = [Mono.Cecil.FieldDefinition]::new(
    '_a',
    [Mono.Cecil.FieldAttributes]::Private,
    $intType
)
$pickerType.Fields.Add($opacityField)

$swatchField = $pickerType.Fields | Where-Object Name -eq '_swatch' | Select-Object -First 1
$initialRgbField = $pickerType.Fields | Where-Object Name -eq '_initialRgb' | Select-Object -First 1

$tryParse = Find-MemberReference 'System.Boolean System.Int32::TryParse(System.String,System.Int32&)'
$clamp = Find-MemberReference 'System.Double System.Math::Clamp(System.Double,System.Double,System.Double)'
$round = Find-MemberReference 'System.Double System.Math::Round(System.Double)'
$redraw = Find-MemberReference 'System.Void Vintagestory.API.Client.GuiElementCustomDraw::Redraw()'

$handlerAttributes = [Mono.Cecil.MethodAttributes] (
    [int] [Mono.Cecil.MethodAttributes]::Private -bor
    [int] [Mono.Cecil.MethodAttributes]::HideBySig
)
$opacityChanged = [Mono.Cecil.MethodDefinition]::new(
    'HandleOpacityChanged',
    $handlerAttributes,
    $voidType
)
$opacityChanged.Parameters.Add([Mono.Cecil.ParameterDefinition]::new(
    'value',
    [Mono.Cecil.ParameterAttributes]::None,
    $stringType
))
$parsedOpacity = [Mono.Cecil.Cil.VariableDefinition]::new($intType)
$opacityChanged.Body.Variables.Add($parsedOpacity)
$opacityChanged.Body.InitLocals = $true
$handlerIl = $opacityChanged.Body.GetILProcessor()
$handlerReturn = $handlerIl.Create([Mono.Cecil.Cil.OpCodes]::Ret)
$handlerIl.Append($handlerIl.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_1))
$handlerIl.Append($handlerIl.Create([Mono.Cecil.Cil.OpCodes]::Ldloca_S, $parsedOpacity))
$handlerIl.Append($handlerIl.Create([Mono.Cecil.Cil.OpCodes]::Call, $tryParse))
$handlerIl.Append($handlerIl.Create([Mono.Cecil.Cil.OpCodes]::Brfalse_S, $handlerReturn))
$handlerIl.Append($handlerIl.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
$handlerIl.Append($handlerIl.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $parsedOpacity))
$handlerIl.Append($handlerIl.Create([Mono.Cecil.Cil.OpCodes]::Conv_R8))
$handlerIl.Append($handlerIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R8, [double] 0))
$handlerIl.Append($handlerIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R8, [double] 100))
$handlerIl.Append($handlerIl.Create([Mono.Cecil.Cil.OpCodes]::Call, $clamp))
$handlerIl.Append($handlerIl.Create([Mono.Cecil.Cil.OpCodes]::Conv_I4))
$handlerIl.Append($handlerIl.Create([Mono.Cecil.Cil.OpCodes]::Stfld, $opacityField))
$handlerIl.Append($handlerIl.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
$handlerIl.Append($handlerIl.Create([Mono.Cecil.Cil.OpCodes]::Ldfld, $swatchField))
$handlerIl.Append($handlerIl.Create([Mono.Cecil.Cil.OpCodes]::Brfalse_S, $handlerReturn))
$handlerIl.Append($handlerIl.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
$handlerIl.Append($handlerIl.Create([Mono.Cecil.Cil.OpCodes]::Ldfld, $swatchField))
$handlerIl.Append($handlerIl.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $redraw))
$handlerIl.Append($handlerReturn)
$opacityChanged.Body.MaxStackSize = 4
$pickerType.Methods.Add($opacityChanged)

# Default to fully opaque, then use an existing fourth color component when one
# is present. Older three-component saved colors remain fully opaque.
$constructor = $pickerType.Methods |
    Where-Object { $_.Name -eq '.ctor' -and $_.Parameters.Count -eq 3 } |
    Select-Object -First 1
$constructorIl = $constructor.Body.GetILProcessor()
$constructorFirst = $constructor.Body.Instructions[0]
$constructorIl.InsertBefore($constructorFirst, $constructorIl.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
$constructorIl.InsertBefore($constructorFirst, $constructorIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_S, [sbyte] 100))
$constructorIl.InsertBefore($constructorFirst, $constructorIl.Create([Mono.Cecil.Cil.OpCodes]::Stfld, $opacityField))

$initialStore = $constructor.Body.Instructions |
    Where-Object { $_.OpCode -eq [Mono.Cecil.Cil.OpCodes]::Stfld -and $_.Operand -eq $initialRgbField } |
    Select-Object -First 1
$afterInitialStore = $initialStore.Next
$cursor = $initialStore
foreach ($instruction in @(
    $constructorIl.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_2),
    $constructorIl.Create([Mono.Cecil.Cil.OpCodes]::Ldlen),
    $constructorIl.Create([Mono.Cecil.Cil.OpCodes]::Conv_I4),
    $constructorIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_3),
    $constructorIl.Create([Mono.Cecil.Cil.OpCodes]::Ble_S, $afterInitialStore),
    $constructorIl.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0),
    $constructorIl.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_2),
    $constructorIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_3),
    $constructorIl.Create([Mono.Cecil.Cil.OpCodes]::Ldelem_R8),
    $constructorIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R8, [double] 100),
    $constructorIl.Create([Mono.Cecil.Cil.OpCodes]::Mul),
    $constructorIl.Create([Mono.Cecil.Cil.OpCodes]::Call, $round),
    $constructorIl.Create([Mono.Cecil.Cil.OpCodes]::Conv_I4),
    $constructorIl.Create([Mono.Cecil.Cil.OpCodes]::Stfld, $opacityField)
)) {
    $constructorIl.InsertAfter($cursor, $instruction)
    $cursor = $instruction
}
$constructor.Body.MaxStackSize = [Math]::Max($constructor.Body.MaxStackSize, 4)

$compose = $pickerType.Methods | Where-Object Name -eq 'ComposeGuis' | Select-Object -First 1
$composeIl = $compose.Body.GetILProcessor()
$hueBoundsVariable = $compose.Body.Variables[12]
$spacingVariable = $compose.Body.Variables[6]
$boundsType = $hueBoundsVariable.VariableType
$opacityLabelBounds = [Mono.Cecil.Cil.VariableDefinition]::new($boundsType)
$opacityInputBounds = [Mono.Cecil.Cil.VariableDefinition]::new($boundsType)
$compose.Body.Variables.Add($opacityLabelBounds)
$compose.Body.Variables.Add($opacityInputBounds)

$belowCopy = Find-MemberReference 'Vintagestory.API.Client.ElementBounds Vintagestory.API.Client.ElementBounds::BelowCopy(System.Double,System.Double,System.Double,System.Double)'
$rightCopy = Find-MemberReference 'Vintagestory.API.Client.ElementBounds Vintagestory.API.Client.ElementBounds::RightCopy(System.Double,System.Double,System.Double,System.Double)'
$withFixedSize = Find-MemberReference 'Vintagestory.API.Client.ElementBounds Vintagestory.API.Client.ElementBounds::WithFixedSize(System.Double,System.Double)'
$whiteSmallText = Find-MemberReference 'Vintagestory.API.Client.CairoFont Vintagestory.API.Client.CairoFont::WhiteSmallText()'
$textInputFont = Find-MemberReference 'Vintagestory.API.Client.CairoFont Vintagestory.API.Client.CairoFont::TextInput()'
$addStaticText = Find-MemberReference 'Vintagestory.API.Client.GuiComposer Vintagestory.API.Client.GuiComposerHelpers::AddStaticText(Vintagestory.API.Client.GuiComposer,System.String,Vintagestory.API.Client.CairoFont,Vintagestory.API.Client.ElementBounds,System.String)'
$addNumberInput = Find-MemberReference 'Vintagestory.API.Client.GuiComposer Vintagestory.API.Client.GuiComposerHelpers::AddNumberInput(Vintagestory.API.Client.GuiComposer,Vintagestory.API.Client.ElementBounds,System.Action`1<System.String>,Vintagestory.API.Client.CairoFont,System.String)'
$getNumberInput = Find-MemberReference 'Vintagestory.API.Client.GuiElementNumberInput Vintagestory.API.Client.GuiComposerHelpers::GetNumberInput(Vintagestory.API.Client.GuiComposer,System.String)'
$setValue = Find-MemberReference 'System.Void Vintagestory.API.Client.GuiElementEditableTextBase::SetValue(System.Single)'
$getSingleComposer = Find-MemberReference 'Vintagestory.API.Client.GuiComposer Vintagestory.API.Client.GuiDialog::get_SingleComposer()'
$langGet = $module.GetType('BlockOverlay.Util.LangUtils').Methods |
    Where-Object { $_.Name -eq 'Get' -and $_.Parameters.Count -eq 2 } |
    Select-Object -First 1
if ($null -eq $langGet) {
    throw 'Could not find BlockOverlay.Util.LangUtils.Get.'
}
$actionStringConstructor = Find-MemberReference 'System.Void System.Action`1<System.String>::.ctor(System.Object,System.IntPtr)'

$arrayEmptyObject = $compose.Body.Instructions |
    Where-Object { $_.Operand -and $_.Operand.ToString() -like '*System.Array::Empty<System.Object>()' } |
    Select-Object -First 1 -ExpandProperty Operand
if ($null -eq $arrayEmptyObject) {
    throw 'Could not find Array.Empty<object>() in ComposeGuis.'
}

# Put the opacity row directly below the hue bar.
$boundsInsertionPoint = $compose.Body.Instructions |
    Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'Vintagestory.API.Client.CairoFont Vintagestory.API.Client.CairoFont::WhiteSmallText()' } |
    Select-Object -First 1
foreach ($instruction in @(
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $hueBoundsVariable),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R8, [double] 0),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $spacingVariable),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R8, [double] 0),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R8, [double] 0),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $belowCopy),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R8, [double] 190),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R8, [double] 30),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $withFixedSize),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $opacityLabelBounds),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $opacityLabelBounds),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R8, [double] 10),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R8, [double] 0),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R8, [double] 0),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R8, [double] 0),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $rightCopy),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R8, [double] 100),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R8, [double] 30),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $withFixedSize),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $opacityInputBounds)
)) {
    $composeIl.InsertBefore($boundsInsertionPoint, $instruction)
}

$saveLabelInstruction = $compose.Body.Instructions |
    Where-Object { $_.OpCode -eq [Mono.Cecil.Cil.OpCodes]::Ldstr -and $_.Operand -eq 'label-save' } |
    Select-Object -First 1
foreach ($instruction in @(
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldstr, 'label-opacity'),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Call, $arrayEmptyObject),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Call, $langGet),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Call, $whiteSmallText),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $opacityLabelBounds),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldnull),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Call, $addStaticText),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $opacityInputBounds),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldftn, $opacityChanged),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Newobj, $actionStringConstructor),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Call, $textInputFont),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldstr, 'opacity'),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Call, $addNumberInput)
)) {
    $composeIl.InsertBefore($saveLabelInstruction, $instruction)
}

$setSingleComposer = $compose.Body.Instructions |
    Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.Void Vintagestory.API.Client.GuiDialog::set_SingleComposer(Vintagestory.API.Client.GuiComposer)' } |
    Select-Object -First 1
$cursor = $setSingleComposer
foreach ($instruction in @(
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Call, $getSingleComposer),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldstr, 'opacity'),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Call, $getNumberInput),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Ldfld, $opacityField),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Conv_R4),
    $composeIl.Create([Mono.Cecil.Cil.OpCodes]::Callvirt, $setValue)
)) {
    $composeIl.InsertAfter($cursor, $instruction)
    $cursor = $instruction
}
$compose.Body.MaxStackSize = [Math]::Max($compose.Body.MaxStackSize, 16)

# Save RGBA instead of discarding the selected alpha value.
$confirm = $pickerType.Methods | Where-Object Name -eq 'HandleConfirm' | Select-Object -First 1
$confirmIl = $confirm.Body.GetILProcessor()
$newArray = $confirm.Body.Instructions |
    Where-Object OpCode -eq ([Mono.Cecil.Cil.OpCodes]::Newarr) |
    Select-Object -First 1
$arrayLength = $newArray.Previous
$arrayLength.OpCode = [Mono.Cecil.Cil.OpCodes]::Ldc_I4_4
$arrayLength.Operand = $null
$invokeCallback = $confirm.Body.Instructions |
    Where-Object { $_.Operand -and $_.Operand.ToString().StartsWith('System.Void System.Action`2<System.Boolean,System.Double[]>::Invoke') } |
    Select-Object -First 1
foreach ($instruction in @(
    $confirmIl.Create([Mono.Cecil.Cil.OpCodes]::Dup),
    $confirmIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_3),
    $confirmIl.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0),
    $confirmIl.Create([Mono.Cecil.Cil.OpCodes]::Ldfld, $opacityField),
    $confirmIl.Create([Mono.Cecil.Cil.OpCodes]::Conv_R8),
    $confirmIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R8, [double] 100),
    $confirmIl.Create([Mono.Cecil.Cil.OpCodes]::Div),
    $confirmIl.Create([Mono.Cecil.Cil.OpCodes]::Stelem_R8)
)) {
    $confirmIl.InsertBefore($invokeCallback, $instruction)
}
$confirm.Body.MaxStackSize = [Math]::Max($confirm.Body.MaxStackSize, 8)

# Fade the new-color half of the preview swatch as opacity changes.
$drawSwatch = $pickerType.Methods | Where-Object Name -eq 'DrawSwatch' | Select-Object -First 1
$drawSwatchIl = $drawSwatch.Body.GetILProcessor()
$setSourceRgbCalls = @($drawSwatch.Body.Instructions |
    Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.Void Cairo.Context::SetSourceRGB(System.Double,System.Double,System.Double)' })
$chosenColorCall = $setSourceRgbCalls[-1]
$setSourceRgba = Find-MemberReference 'System.Void Cairo.Context::SetSourceRGBA(System.Double,System.Double,System.Double,System.Double)'
foreach ($instruction in @(
    $drawSwatchIl.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0),
    $drawSwatchIl.Create([Mono.Cecil.Cil.OpCodes]::Ldfld, $opacityField),
    $drawSwatchIl.Create([Mono.Cecil.Cil.OpCodes]::Conv_R8),
    $drawSwatchIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R8, [double] 100),
    $drawSwatchIl.Create([Mono.Cecil.Cil.OpCodes]::Div)
)) {
    $drawSwatchIl.InsertBefore($chosenColorCall, $instruction)
}
$chosenColorCall.Operand = $setSourceRgba
$drawSwatch.Body.MaxStackSize = [Math]::Max($drawSwatch.Body.MaxStackSize, 8)

# The game's Doubles2Hex helper intentionally emits only #RRGGBB. Append the
# alpha byte ourselves so Hex2Doubles restores the saved per-target opacity.
$saveCallbackType = $module.GetType(
    'BlockOverlay.Gui.GuiElementSearchResultListItem/<>c__DisplayClass26_0'
)
$saveColorCallback = $saveCallbackType.Methods |
    Where-Object Name -eq '<OnMouseUpOnElement>b__0' |
    Select-Object -First 1
$doublesToHexCall = $saveColorCallback.Body.Instructions |
    Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.String Vintagestory.API.MathTools.ColorUtil::Doubles2Hex(System.Double[])' } |
    Select-Object -First 1
if ($null -eq $doublesToHexCall) {
    throw 'Could not find the color serialization call in the picker callback.'
}

$alphaByte = [Mono.Cecil.Cil.VariableDefinition]::new($intType)
$saveColorCallback.Body.Variables.Add($alphaByte)
$saveColorIl = $saveColorCallback.Body.GetILProcessor()
$stringFormat = [Mono.Cecil.MethodReference]::new(
    'Format',
    $stringType,
    $stringType
)
$stringFormat.HasThis = $false
$stringFormat.Parameters.Add([Mono.Cecil.ParameterDefinition]::new($stringType))
$stringFormat.Parameters.Add([Mono.Cecil.ParameterDefinition]::new($module.TypeSystem.Object))
$concatStrings = Find-MemberReference 'System.String System.String::Concat(System.String,System.String)'
$cursor = $doublesToHexCall
foreach ($instruction in @(
    $saveColorIl.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_2),
    $saveColorIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_3),
    $saveColorIl.Create([Mono.Cecil.Cil.OpCodes]::Ldelem_R8),
    $saveColorIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_R8, [double] 255),
    $saveColorIl.Create([Mono.Cecil.Cil.OpCodes]::Mul),
    $saveColorIl.Create([Mono.Cecil.Cil.OpCodes]::Call, $round),
    $saveColorIl.Create([Mono.Cecil.Cil.OpCodes]::Conv_I4),
    $saveColorIl.Create([Mono.Cecil.Cil.OpCodes]::Stloc, $alphaByte),
    $saveColorIl.Create([Mono.Cecil.Cil.OpCodes]::Ldstr, '{0:X2}'),
    $saveColorIl.Create([Mono.Cecil.Cil.OpCodes]::Ldloc, $alphaByte),
    $saveColorIl.Create([Mono.Cecil.Cil.OpCodes]::Box, $intType),
    $saveColorIl.Create([Mono.Cecil.Cil.OpCodes]::Call, $stringFormat),
    $saveColorIl.Create([Mono.Cecil.Cil.OpCodes]::Call, $concatStrings)
)) {
    $saveColorIl.InsertAfter($cursor, $instruction)
    $cursor = $instruction
}
$saveColorCallback.Body.MaxStackSize = [Math]::Max($saveColorCallback.Body.MaxStackSize, 5)

# The added serialization instructions push this jump beyond the signed-byte
# range used by brfalse.s. Promote it so the CLR receives valid IL.
$confirmedBranch = $saveColorCallback.Body.Instructions |
    Where-Object OpCode -eq ([Mono.Cecil.Cil.OpCodes]::Brfalse_S) |
    Select-Object -First 1
if ($null -eq $confirmedBranch -or $null -eq $confirmedBranch.Operand) {
    throw 'Could not find the Save callback confirmation branch.'
}
$confirmedBranch.OpCode = [Mono.Cecil.Cil.OpCodes]::Brfalse

# Configured colors are parsed as RGBA, but upstream immediately overwrites the
# parsed alpha with 1.0. Keep the value supplied by the eight-digit hex string.
$builtinColorMap = $module.GetType('BlockOverlay.Util.BuiltinColorMap')
$findConfiguredColor = $builtinColorMap.Methods |
    Where-Object Name -eq 'FindRgbaColorForCode' |
    Select-Object -First 1
$hexToDoublesCall = $findConfiguredColor.Body.Instructions |
    Where-Object { $_.Operand -and $_.Operand.ToString() -eq 'System.Double[] Vintagestory.API.MathTools.ColorUtil::Hex2Doubles(System.String)' } |
    Select-Object -First 1
if ($null -eq $hexToDoublesCall) {
    throw 'Could not find configured-color deserialization.'
}
$parsedColorStore = $hexToDoublesCall.Next
$alphaResetStore = $parsedColorStore.Next
while ($null -ne $alphaResetStore -and $alphaResetStore.OpCode -ne [Mono.Cecil.Cil.OpCodes]::Stelem_R8) {
    $alphaResetStore = $alphaResetStore.Next
}
if ($null -eq $alphaResetStore) {
    throw 'Could not find the upstream configured-color alpha reset.'
}
$parsedColorReturn = $alphaResetStore.Next
$colorMapIl = $findConfiguredColor.Body.GetILProcessor()
$instructionToRemove = $parsedColorStore.Next
while ($instructionToRemove -ne $parsedColorReturn) {
    $nextInstruction = $instructionToRemove.Next
    $colorMapIl.Remove($instructionToRemove)
    $instructionToRemove = $nextInstruction
}

# These private enum constants are inlined in IL. Removing their unused field
# metadata lets Cecil write the mod without requiring a local game install.
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
Write-Output "Color-picker opacity patch written to $outputFullPath"
