using ChronoTriggerAccessibility.Native.Capture;

namespace ChronoTriggerAccessibility.Mod.Navigation;

/// <summary>Native identity and introduction proof for recurring field NPCs.
/// A handled null name deliberately suppresses an older, premature name provider.</summary>
public static class RecurringNpcLabels
{
    public readonly record struct Identity(bool Handled, string? Name);
    public static Identity Resolve(int scene, FieldActorSnapshot actor, FieldStoryState? state) =>
        (scene,actor.Index,actor.ClassTag,actor.VisualIndex) switch
        {
            (329,12,4,9) or (332,9,4,9) => Named("Schala",Flag(state,0xF4,0x10)),
            (328,9,4,10) => Named("Janus",Flag(state,0xF4,0x10)),
            (355,30,4,10) => Named("Janus",Flag(state,0xF4,0x10)||state is{Point:>=0xAB}),
            (116,19,4,5) => Named("Toma",TomaKnown(state)),
            (138,21,4,5) => Named("Toma",Flag(state,0xF2,1)),
            (155,18,4,5) => Named("Toma",Flag(state,0xF2,2)),
            (188,13,4,5) => Named("Toma",TomaKnown(state)||Flag(state,0x1AF,2)),
            (63,9,4,5) => Named("Toma",Flag(state,0x1AC,0x40)),
            (4,8,4,0x27) => Named("Taban",state is{Point:>=0x0A}),
            (9,9,4,0xD5) => Named("Lara",Flag(state,0x5A,4)||Flag(state,0x5A,0x40)||Flag(state,0x7C,0x20)),
            // Outside the 990 AD mode this slot has no native position assignment.
            (4,11,4,0xD5) => Named("Lara",Flag(state,0x5A,2)&&(Flag(state,0x5A,0x40)||Flag(state,0x7C,0x20))),
            (40,8,4,0) or (41,8,4,0) or (440,23,4,0) => Named("Melchior",state is{Point:>=0x6C}),
            // His optional fair introduction records no native persistent proof.
            (5,14,4,0) => new(true,null),
            (242,16,5 or 6,0) => new(true,Flag(state,0x7C,1)||Flag(state,0x170,4)?"Nu, Belthasar's recording":"Nu, Strange Construct"),
            // The Cape explicitly names the Guru of Time; his own confirmation
            // then links that identity to this old man, even in an Ayla-only party.
            (464,28,4,0x48) => new(true,Flag(state,0xD8,2)&&
                (Flag(state,0x138,1)||Flag(state,0x138,2))?"Gaspar":"Old man under the lamp"),
            (465,10,5 or 6,>=0xE0 and <=0xE5) => Named("Spekkio",state is{Point:>=0x4C}),
            (152,10,4,4) => Named("Tata",state is{Point:>=0x66}),
            (169,11,5 or 6,0x9D) => Named("Slash",Flag(state,0xA3,0x40)),
            (180,9,5 or 6,0xA5) or (184,9,5 or 6,0xBA) => Named("Slash",state is{Point:>=0x8A}),
            (180,10,5 or 6,0xA0) or (183,9,5 or 6,0xBB) => Named("Flea",state is{Point:>=0x8A}),
            (289,8,5 or 6,0x9A) or (300,8,5 or 6,0x9A) or (301,9,5 or 6,0x9A) => Named("Azala",state is{Point:>=0x7C}),
            _ => default,
        };

    private static Identity Named(string name,bool introduced)=>new(true,introduced?name:null);
    private static bool Flag(FieldStoryState? state,int index,int mask)=>state?.Flag(index,mask)==true;
    private static bool TomaKnown(FieldStoryState? state)=>
        Flag(state,0x144,0x10)||Flag(state,0xF2,1)||Flag(state,0x1A0,2);
}
