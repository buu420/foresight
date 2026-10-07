using System.Collections.Immutable;
using Reloaded.Hooks.Definitions.X86;

namespace ChronoTriggerAccessibility.Native.Hooks;

public enum HookId
{
    TextManagerGetMsg,
    OpeTextResolver,
    SceneManagerCreate,
    SceneManagerNextScene,
    ModeSelectSteamInit,
    OpeManualSceneInit,
    NameInputSceneInit,
    NameInputSceneUpdate,
    NameConfirmationBuilder,
    TitleMenuModeEnter,
    TitleRowFactory,
    TitleSceneUpdate,
    TitleMenuCallback,
    NsMenuFocusSetter,
    NsMenuCustomButtonConstructor,
    NsMenuControlBinder,
    ModeSelectCallback,
    ControlNextCallback,
    NameActionCallback,
    NameDirectEntryActivation,
    NameDirectEntryClose,
    NameGridRefresh,
    GallerySceneSwitchNode,
    ExtrasHubOnEnter,
    EndingLogOnEnter,
    EndingDetailOnEnter,
    ExtrasNodeOnExit,
    ExtrasHubDeletingDestructor,
    EndingLogDeletingDestructor,
    ExtrasHubCallback,
    EndingLogCallback,
    EndingDetailCallback,
    ExtrasLogTransition,
    ExtrasDetailTransition,
    SaveLoadConfirmationBuilder,
    SaveLoadNodeDestructor,
    MenuNodeConfigSteamConstructor,
    MenuNodeConfigSteamBuilder,
    MenuNodeConfigSteamDestructor,
    MenuNodeConfigSteamPageBuilder,
    SteamSettingsCategoryCallback,
    SteamSettingsRowCallback,
    SteamSettingsLicenseCallback,
    SteamSettingsSetterInvoker,
    SteamSettingsResolutionBuilder,
    SteamSettingsResolutionCallback,
    SteamSettingsConfirmationBuilderA,
    SteamSettingsConfirmationBuilderB,
    SteamSettingsLicensePageBuilder,
    SteamSettingsConfirmationCallbackA,
    SteamSettingsConfirmationCallbackB,
    SteamSettingsControllerBuilder,
    SteamSettingsControllerRowRefresh,
    SteamSettingsControllerCallback,
    SteamSettingsKeyboardBuilder,
    SteamSettingsKeyboardRowRefresh,
    SteamSettingsKeyboardCallback,
    MenuNodeConfigConstructor,
    MenuNodeConfigBuilder,
    MenuNodeConfigDestructor,
    TouchSettingsPageTransition,
    TouchSettingsCallback,
    TouchSettingsValueMutation,
    TouchSettingsConfirmationBuilderA,
    TouchSettingsConfirmationBuilderB,
    TouchSettingsConfirmationCallbackA,
    TouchSettingsConfirmationCallbackB,
    SteamSettingsCategoryLabelCallSite,
    SteamSettingsRowLabelCallSite,
    SteamSettingsTitleResolutionValueLabelCallSite,
    SteamSettingsSelectedValueLabelCallSite,
    SteamSettingsResolutionHeadingLabelCallSite,
    SteamSettingsResolutionEntryLabelCallSite,
    SteamSettingsConfirmationAChoiceLabelCallSite,
    SteamSettingsConfirmationBChoiceLabelCallSite,
    TouchSettingsFirstPermanentControlLabelCallSite,
    TouchSettingsSecondPermanentControlLabelCallSite,
    TouchSettingsHeadingLabelCallSite,
    TouchSettingsConfirmationAChoiceLabelCallSite,
    TouchSettingsConfirmationBChoiceLabelCallSite,
    MsgWindowOpen,
    MsgWindowUpdate,
    MsgWindowClose,
    MsgWindowChoiceConfirmCallSite,
    ClassicTopMenuBuilder,
    TouchTopMenuBuilder,
    MenuTextLabelFactory,
    StatusBarFormatScope,
    StatusBarGlyphRenderer,
    StatusBarDestructor,
    ClassicTopMenuDeletingDestructor,
    TouchTopMenuDeletingDestructor,
    ClassicTopMenuActionDispatcher,
    TouchTopMenuActionDispatcher,
    ClassicTopMenuTimeLabelCallSite,
    ClassicTopMenuCurrencyLabelCallSite,
    ClassicTopMenuSingleFooterLabelCallSite,
    ClassicTopMenuFirstFooterLabelCallSite,
    ClassicTopMenuSecondFooterLabelCallSite,
    ClassicTopMenuContextLabelCallSite,
    StatusBarHiddenLabelCallSite,
    StatusBarEmptyLineLabelCallSite,
    TouchStatusBarInitialLabelCallSite,
    FieldOpcodeDispatcher,
    ClassicTopMenuCaptionLabelCallSite,
    ClassicTopMenuMemberNameLabelCallSite,
    ClassicStatusRowLabelCallSite,
    ClassicStatusRowZeroValueCallSite,
    ClassicStatusUnavailableValueCallSite,
    ClassicStatusCurrentValueCallSite,
    ClassicStatusMaximumValueCallSite,
    ClassicStatusExtraLabelCallSite,
    TouchTopMenuTimeLabelCallSite,
    TouchTopMenuCurrencyLabelCallSite,
    TouchTopMenuCaptionLabelCallSite,
    TouchTopMenuMemberNameLabelCallSite,
    TouchTopMenuReserveNameLabelCallSite,
    CompactStatusRowLabelCallSite,
    CompactStatusRowZeroValueCallSite,
    CompactStatusCurrentValueCallSite,
    CompactStatusMaximumValueCallSite,
    CompactStatusExtraLabelCallSite,
    StatusBarGlyphRendererCallSite,
    FieldNavigationPadCallSite,
    WorldNavigationTickCallSite,
    WorldNavigationPadInstruction,
    WorldNavigationPadGateInstruction,
    WorldNavigationPadSecondInstruction,
    BattleHudRefresh,
    BattleMenuDestructor,
    BattleMessageDisplay,
    BattleDamageNumber,
    BattleMiss,
    BattleDamageRender,
    ClassicFieldMenuReplace,
    TouchFieldMenuReplace,
    SaveSlotOpen,
    MenuManagerDispatch,
    InventoryHelpRefresh,
    SaveSlotDetailsRefresh,
    MenuManagerUpdate,
    WorldNavigationEpochTickCallSite,
    WorldNavigationDactylTickCallSite,
    WorldNavigationEpochPadGateInstruction,
    WorldNavigationEpochPadInstruction,
    WorldNavigationDactylPadGateInstruction,
    WorldNavigationDactylPadInstruction,
    TimeGaugeSceneInit,
    TimeGaugeSceneUpdate,
    ShopSceneUpdate,
    ShopSceneDestructor,
    BikeRaceUpdate,
    BikeRaceDestructor,
    EndingResultDialogBuilder,
    SaveEndingResultSceneDestructor,
    EndingConfirmationBuilder,
    EndingSavingNotice,
    EndingSaveCompleteNotice,
    SaveLoadNoticeWindow,
    SteamSettingsRestartNotice,
    GalleryHubDispatch,
    GalleryMoviesDispatch,
    GallerySoundBack,
    GalleryIllustrationsBack,
    ExtrasMoviesOnEnter,
    ExtrasSoundOnEnter,
    ExtrasIllustrationsOnEnter,
    ExtrasMoviesCallback,
    ExtrasSoundCallback,
    ExtrasIllustrationsCallback,
    ExtrasIllustrationViewerCallback,
    ExtrasSoundIdle,
    GameKeyboardStateFilter,
    GameJoystickStateFilter,
    FormationSceneInit,
    FormationSceneDestructor,
}

public enum X86CallingConvention
{
    MicrosoftThiscall,
    MicrosoftFastcall,
}

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void SubmenuNodeWordDelegate(nint node, nint value);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ShopSceneUpdateDelegate(nint scene, float deltaSeconds);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint ShopSceneDestructorDelegate(nint scene, uint deletingFlags);

// FormationSteamScene::init at RVA 0x2A49A0 (cocos2d::Layer::init override, vtable 0x3B0A48
// slot 158). SceneManager::create(0x19) reaches it through 0x2A4910 when [0x81B4C4]+0x13FDC is
// nonzero; NextScene action 8 pushes that scene for field request 5 (script C8 00). It builds one
// ClassicMenuNodeFormation (0x1BE730, builder 0x1BE850(0)), stores the close std::function at
// node+0x298 and addChilds the node to ECX, never to the field menu. Returns AL; RET.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate byte FormationSceneInitDelegate(nint scene);

// FormationSteamScene deleting destructor, vtable slot 0 at RVA 0x2A4960. It reinstalls the class
// vtable, clears [0x81B4C4]+0x10F84, runs cocos2d::Layer::~Layer, frees the 0x290-byte object
// when bit 0 is set and returns the scene. RET 4.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint FormationSceneDestructorDelegate(nint scene, uint deletingFlags);

// SceneSpecialRace is a SpecialEventImpl, not a cocos Scene::update(float).
// 2EC620 returns AL and takes only ECX; 2EC450 is its non-deleting destructor.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate byte BikeRaceUpdateDelegate(nint scene);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void BikeRaceDestructorDelegate(nint scene);

// SaveEndingResultScene (scene 0x1B) message window at RVA 0x2B1390. Callers: the first-clear
// message 0x2B0CF0, the ending result 0x2B0F30, the Dreamseeker message 0x2B1150 and the
// "Save failed." path 0x2B1B40. ECX is the scene; [EBP+8] is the composed MSVC UTF-8 message,
// which 0x2B06B0 splits on 0x5C into one label per line; [EBP+0xC] is the continuation
// std::function the shared window factory 0x23D520 runs on confirm. RET 8.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void EndingResultDialogBuilderDelegate(nint scene, nint text, nint continuation);

// SaveEndingResultScene deleting destructor, vtable slot 0 at RVA 0x2B09F0. It reinstalls the
// class vtable (RVA 0x3B34D8) at 0x2B0A16, frees the 0x2D8-byte object when bit 0 is set, and
// returns the scene. RET 4.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint SaveEndingResultSceneDestructorDelegate(nint scene, uint deletingFlags);

// SaveEndingResultScene Yes/No window at RVA 0x2B2200, called by "Save game completion data?"
// 0x2B1620, "Return to the title screen?" 0x2B19A0 and "Overwrite existing save data?" 0x2B1A70.
// ECX is the scene (kept at [EBP-0x48]); [EBP+8] the container node scene + 0x29C; [EBP+0xC] the
// localized prompt, rendered by 0x2B06B0; [EBP+0x10] the std::function<void(int)> that receives
// the chosen key. Buttons come from 0x2B25C0 and focus key 1 is set by the call at 0x2B22E1. RET 0xC.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void EndingConfirmationBuilderDelegate(nint scene, nint container, nint prompt, nint callback);

// SaveEndingResultScene "Saving data." notice at RVA 0x2B1F10, reached through _Do_call 0x2B3160
// (ADD ECX,4) of the lambda built by the save gate 0x2B1E20. ECX is that capture: +0 the gate
// node, +4 the scene. The stack holds the gate window's nsInput::Manager and close function. RET 8.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void EndingSavingNoticeDelegate(nint capture, nint manager, nint close);

// SaveEndingResultScene "Save complete." notice at RVA 0x2B2020, reached through _Do_call 0x2B31A0
// (ADD ECX,4) half a second after 0x2B1F10. ECX is that capture: +0 the "Saving data." text node,
// which it removes first, +4 the gate node, +8 the scene. RET.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void EndingSaveCompleteNoticeDelegate(nint capture);

// nsMenu::MenuNodeSaveLoadSteam notice window at RVA 0x21AA40. ECX is the node; [EBP+8] is the
// MSVC UTF-8 text, split on 0x5C by 0x40FC80 into one font-0x0C label per line. The confirmation
// builder 0x21A1D0 draws its prompt with it; 0x21AFC0, 0x21B5C0, 0x21B420, 0x21D4E0 and 0x21D880
// draw the saving, loading, completion and bookmark notices. Returns the text pointer in EAX
// (0x21AEDF). RET 4.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint SaveLoadNoticeWindowDelegate(nint node, nint text);

// nsMenu::MenuNodeConfigSteam restart notice at RVA 0x1F52C0, called by the category callback
// 0x1F0000 (0x1F00AF) and 0x1F0280 (0x1F0302) when the screen mode or size differs from the saved
// one. ECX is the node (kept at [EBP-0x3C], window added to it at 0x1F545D); [EBP+8] is the
// node's layer at +0x290 that receives the dimming backdrop. It resolves (0x3A, 1) from
// msg/resolution.txt (0x1F5403), draws it through 0x2B06B0 (0x1F5410) and builds the shared
// one-control window 0x23D520 (0x1F5455), whose callback 0x1FC240 calls Director::end. RET 4.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void SteamSettingsRestartNoticeDelegate(nint node, nint backdropParent);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate byte SaveSlotOpenDelegate(nint node, int mode, int showBack);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void SubmenuManagerDispatchDelegate(nint manager, int action, int key);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void BattleMenuMemberDelegate(nint menu);

// Native string/vector temporaries occupy unused raw stack words. Forward every
// word unchanged: both presenters clean up 0x1C bytes even where Ghidra omits args.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void BattleSevenWordDelegate(nint menu, nint a1, nint a2, nint a3, nint a4, nint a5, nint a6, nint a7);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void BattleMissDelegate(nint menu, nint a1, nint a2, nint a3, nint a4);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void BattleRenderDelegate(nint menu, nint a1, nint a2);

public enum NativeHookKind
{
    FunctionEntry,
    AssemblyCallSite,
    AssemblyInstructionSite,
}

public sealed class HookContract
{
    public HookContract(
        HookId id,
        string symbol,
        uint rva,
        ReadOnlySpan<byte> expectedBytes,
        Type delegateType,
        X86CallingConvention callingConvention)
        : this(
            id,
            symbol,
            rva,
            expectedBytes,
            NativeHookKind.FunctionEntry,
            delegateType,
            callingConvention)
    {
    }

    public HookContract(
        HookId id,
        string symbol,
        uint rva,
        ReadOnlySpan<byte> expectedBytes,
        NativeHookKind kind,
        Type? delegateType,
        X86CallingConvention? callingConvention)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported native hook kind.");
        }
        if (expectedBytes.IsEmpty)
        {
            throw new ArgumentException("A hook contract must include at least one expected byte.", nameof(expectedBytes));
        }

        if (kind == NativeHookKind.FunctionEntry)
        {
            if (delegateType is null || callingConvention is null || !Enum.IsDefined(callingConvention.Value))
            {
                throw new ArgumentException(
                    "A function-entry hook requires both a delegate type and one supported x86 calling convention.");
            }
            if (!typeof(Delegate).IsAssignableFrom(delegateType))
            {
                throw new ArgumentException("Hook delegate type must derive from System.Delegate.", nameof(delegateType));
            }
        }
        else if (delegateType is not null || callingConvention is not null)
        {
            throw new ArgumentException(
                "An assembly call-site contract is not callable and cannot declare a delegate or function convention.");
        }

        Id = id;
        Symbol = symbol;
        Rva = rva;
        ExpectedBytes = ImmutableArray.Create(expectedBytes.ToArray());
        Kind = kind;
        DelegateType = delegateType;
        CallingConvention = callingConvention;
    }

    public HookId Id { get; }

    public string Symbol { get; }

    public uint Rva { get; }

    public ImmutableArray<byte> ExpectedBytes { get; }

    public NativeHookKind Kind { get; }

    public Type? DelegateType { get; }

    public X86CallingConvention? CallingConvention { get; }
}

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint TextManagerGetMsgDelegate(nint textManager, nint result, int fileId, int messageId);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint OpeTextResolverDelegate(nint resolver, nint result, int bank, int messageId);

[Function(CallingConventions.Fastcall)]
public delegate nint SceneManagerCreateDelegate(int sceneId, int argument);

[Function(CallingConventions.Fastcall)]
public delegate void SceneManagerNextSceneDelegate(uint action);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate byte ModeSelectSteamInitDelegate(nint scene);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate byte OpeManualSceneInitDelegate(nint scene);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate byte NameInputSceneInitDelegate(nint scene);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void NameInputSceneUpdateDelegate(nint scene, float deltaSeconds);

// The caller places one 24-byte x86 MSVC std::string object on the stack by value.
// Six words preserve that exact stack layout and the native callee's ret 0x18 cleanup.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void NameConfirmationBuilderDelegate(
    nint scene,
    uint stringWord0,
    uint stringWord1,
    uint stringWord2,
    uint stringWord3,
    uint stringLength,
    uint stringCapacity);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void TitleMenuModeEnterDelegate(nint mode);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint TitleRowFactoryDelegate(nint labelRecord);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void TitleSceneUpdateDelegate(nint scene, float deltaSeconds);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void TitleMenuCallbackDelegate(nint closure, nint eventTypePointer, nint rowIndexPointer);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void NsMenuFocusSetterDelegate(nint manager, int newIndex);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint NsMenuCustomButtonConstructorDelegate(nint storage);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void NsMenuControlBinderDelegate(nint manager, nint focusableState, int managerKey);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ModeSelectCallbackDelegate(nint closure, int eventType, int controlOrDirection);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ControlNextCallbackDelegate(nint closure, nint eventTypePointer, nint valuePointer);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void NameActionCallbackDelegate(nint closure, int eventType, int actionId);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void NameDirectEntryActivationDelegate(nint capture);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void NameDirectEntryCloseDelegate(nint capture);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void NameGridRefreshDelegate(nint closure);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint GallerySceneSwitchNodeDelegate(nint galleryScene, int action, uint rawStackWord1);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ExtrasHubOnEnterDelegate(nint node);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void EndingLogOnEnterDelegate(nint node);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void EndingDetailOnEnterDelegate(nint node);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ExtrasNodeOnExitDelegate(nint node);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint ExtrasHubDeletingDestructorDelegate(nint node, uint deletingFlags);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint EndingLogDeletingDestructorDelegate(nint node, uint deletingFlags);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ExtrasHubCallbackDelegate(nint closure, int eventType, int action);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void EndingLogCallbackDelegate(nint closure, int eventType, int action);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void EndingDetailCallbackDelegate(nint closure, int eventType, int action);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ExtrasLogTransitionDelegate(nint payload);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ExtrasDetailTransitionDelegate(nint payload);

// GalleryScene deferred Hub action at RVA 0x2A5650, reached one frame after the Hub callback
// through 0x2A5590 → Sequence → _Do_call 0x2A73C0. ECX is the lambda capture {Hub key, scene}.
// Key 0 switches to Movies (switchNode(1, 0) at 0x2A5699); keys 1, 2 and 3 construct
// Illustrations (0x1D5090), Sound (0x1D8ED0) and the Ending Log (0x1D3820) directly and attach
// them with 0x2A5270; key 4 calls NextScene(-1) (0x2A5981). RET.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void GalleryHubDispatchDelegate(nint payload);

// GalleryScene deferred Movies action at RVA 0x2A5A80 (_Do_call 0x2A7330). ECX is {action,
// scene}: 0 calls NextScene(0), which pushes PlayMovieScene 0x1C (0x29820B); 4 detaches the page
// and attaches switchNode(0, 0) (0x2A5ABB). RET.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void GalleryMoviesDispatchDelegate(nint payload);

// GalleryScene deferred Sound Back at RVA 0x2A5BD0 (_Do_call 0x2A72A0). ECX is {scene}. It
// constructs the Hub directly with focus 2 (0x2A5C3E), titles it (0x41, 6) and attaches it. RET.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void GallerySoundBackDelegate(nint payload);

// GalleryScene deferred Illustrations Back at RVA 0x2A6D30 (_Do_call 0x2A7100). ECX is {scene}.
// It constructs the Hub directly with focus 1 (0x2A6D9E), titles it (0x41, 6) and attaches it. RET.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void GalleryIllustrationsBackDelegate(nint payload);

// GalleryNodeMovieTop::onEnter, vtable slot 99 at RVA 0x1D7370: Node::onEnter, then the builder
// 0x1D7390 with the row at node + 0x2C8. RET.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ExtrasMoviesOnEnterDelegate(nint node);

// GalleryNodeSoundTop::onEnter, vtable slot 99 at RVA 0x1D9050: Node::onEnter, then a tail jump to
// the builder 0x1D9070.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ExtrasSoundOnEnterDelegate(nint node);

// GalleryNodeIllustTop::onEnter, vtable slot 99 at RVA 0x1D5150: Node::onEnter, then the builder
// 0x1D51A0. RET.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ExtrasIllustrationsOnEnterDelegate(nint node);

// Movies input handler at RVA 0x1D83C0 (_Do_call 0x1D8D50). ECX is {manager, node, controls};
// [EBP+8] the nsInput event (0 decide, 1 focus, 2 cancel), [EBP+0xC] the manager key. RET 8.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ExtrasMoviesCallbackDelegate(nint closure, int eventType, int action);

// Sound input handler at RVA 0x1D9F70 (_Do_call 0x1DAE90). ECX is {manager, node, controls}. RET 8.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ExtrasSoundCallbackDelegate(nint closure, int eventType, int action);

// Illustrations list input handler at RVA 0x1D6160 (_Do_call 0x1D7130). ECX is {manager, node,
// container, controls}. RET 8.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ExtrasIllustrationsCallbackDelegate(nint closure, int eventType, int action);

// Illustration viewer input handler at RVA 0x1D6890 (_Do_call 0x1D6F50). ECX is {image, node,
// layout, viewer manager} as captured at 0x1D6778; decide or cancel closes the viewer. RET 8.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ExtrasIllustrationViewerCallbackDelegate(nint closure, int eventType, int action);

// Sound idle state at RVA 0x1DA820: unschedules update, sets the StatusBar to (0x1A, 0x3D),
// clears the Now Playing labels at +0x2DC/+0x2E0 and the playing byte +0x2E4. Called by the
// builder, by Cancel while playing and by update 0x1DA9B0 when the track ends. RET.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ExtrasSoundIdleDelegate(nint node);

// nsMenu::MenuNodeSaveLoadSteam::openConfirm at RVA 0x21A1D0. The mode that selects the
// prompt lives on the node at +0x2CC; the slot argument is the save file the confirmation
// is about, and is 0 for the Resume and bookmark modes that confirm on open.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void SaveLoadConfirmationBuilderDelegate(nint node, int slot);

// nsMenu::MenuNodeSaveLoadSteam::~MenuNodeSaveLoadSteam at RVA 0x218860, the body the
// deleting destructor at 0x218930 calls. It rewrites the vtable at 0x218888.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void SaveLoadNodeDestructorDelegate(nint node);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint MenuNodeConfigSteamConstructorDelegate(nint instance, int context);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void MenuNodeConfigSteamBuilderDelegate(nint instance);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void MenuNodeConfigSteamDestructorDelegate(nint instance);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void MenuNodeConfigSteamPageBuilderDelegate(nint root, uint pageIndex);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void SteamSettingsCallbackDelegate(nint context, int eventType, int key);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void SteamSettingsSetterInvokerDelegate(nint setter, int newIndex);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void SteamSettingsNestedBuilderDelegate(nint root);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void SteamSettingsLicensePageBuilderDelegate(nint root, int pageIndex);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void SteamSettingsRowRefreshDelegate(nint context, int rowIndex);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint MenuNodeConfigConstructorDelegate(nint root);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void MenuNodeConfigBuilderDelegate(nint root);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void MenuNodeConfigDestructorDelegate(nint root);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint TouchSettingsPageTransitionDelegate(nint closure, int delta);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void TouchSettingsCallbackDelegate(nint context, int eventType, int key);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate byte TouchSettingsValueMutationDelegate(nint root, int page, int row, int proposedIndex);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void TouchSettingsNestedBuilderDelegate(nint root);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void MsgWindowOpenDelegate(nint msgWindow, uint rawStackWord);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void MsgWindowUpdateDelegate(nint msgWindow, uint deltaSecondsBits);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void MsgWindowCloseDelegate(nint msgWindow, uint dummyStackWord);

[Function(CallingConventions.Cdecl)]
public delegate void MsgWindowChoiceConfirmProbeDelegate(nint msgWindow);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void ClassicTopMenuBuilderDelegate(nint topMenu, uint rawStackWord);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void TouchTopMenuBuilderDelegate(nint topMenu, uint rawStackWord);

[Function(CallingConventions.Fastcall)]
public delegate nint MenuTextLabelFactoryDelegate(nint position, nint msvcUtf8String, nint anchor, int fontSize);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void StatusBarFormatScopeDelegate(nint statusBar, nint msvcUtf8String, uint rawMode);

[Function(CallingConventions.Fastcall)]
public delegate nint StatusBarGlyphRendererDelegate(nint glyphOutput, nint msvcUtf16String, nint outputArgument);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void StatusBarDestructorDelegate(nint statusBar);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint ClassicTopMenuDeletingDestructorDelegate(nint topMenu, uint deletingFlags);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate nint TouchTopMenuDeletingDestructorDelegate(nint topOrEndingDetail, uint deletingFlags);

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void TopMenuActionDispatcherDelegate(nint context);

[Function(CallingConventions.Cdecl)]
public delegate void NativeCallSiteProbeDelegate();

[Function(CallingConventions.MicrosoftThiscall)]
public delegate void FieldOpcodeDispatcherDelegate(nint context, int opcode);

[Function(CallingConventions.Cdecl)]
public delegate uint FieldNavigationPadProbeDelegate(nint engine, uint originalPad);

[Function(CallingConventions.Cdecl)]
public delegate void GameKeyboardStateProbeDelegate(nint keyboardState);

[Function(CallingConventions.Cdecl)]
public delegate void GameJoystickStateProbeDelegate(uint deviceId, nint joystickState, uint result);

// AgeSelectScene::init at RVA 0x2989B0 (cocos2d::Layer::init override, vtable 0x3AF778
// slot 158). The time gauge is created through SceneManager::create(29) when the world
// scene dispatches NextScene action 5 for master action 8.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate byte TimeGaugeSceneInitDelegate(nint scene);

// AgeSelectScene::update(float) at RVA 0x2996D0 (vtable slot 122). It polls the input
// manager directly, moves the highlighted slot at +0x2B8, commits through 0x29A150 and
// sets the closing flag at +0x17E4 before NextScene(0) pops the scene.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate void TimeGaugeSceneUpdateDelegate(nint scene, float deltaSeconds);

[Function(CallingConventions.Cdecl)]
public delegate void SteamSettingsRenderedValueProbeDelegate(nint text);

// Callable wrapper for the audited std::function<int()>::_Do_call target. Reloaded
// supplies the x86 thiscall-to-managed wrapper; the target object is passed in ECX.
[Function(CallingConventions.MicrosoftThiscall)]
public delegate int ModeValueGetterDelegate(nint target);
