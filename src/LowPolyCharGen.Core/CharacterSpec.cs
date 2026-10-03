using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LowPolyCharGen;

/// <summary>Which skeleton the character is rigged to, which also sets its proportions.</summary>
public enum RigTarget { ToonSoldiers, Mannequin }
public enum Gender { Male, Female }

/// <summary>Uniform edition: a theatre palette for the clothes, gear and camouflage. Custom keeps your own colours.</summary>
public enum Edition { Custom, Desert, Snow, Jungle, Civilian, Militia }
/// <summary>Face covering: a knitted balaclava with an eye slit, or a shemagh wrapped over the lower face.</summary>
public enum FaceCover { None, Balaclava, Shemagh }
/// <summary>Which garments the camouflage is printed on (militia often wear camo trousers with a plain top).</summary>
public enum CamoCoverage { All, TrousersOnly, TopOnly }
public enum HeadShape { Standard, Square, Round, Slim, Heavy }
public enum HairStyle { Bald, Buzz, Short, Bob, Long, Ponytail, Bun, Mohawk }
public enum FacialHair { None, Stubble, Moustache, Goatee, Beard, BushyBeard }
public enum Camouflage { None, Woodland, Digital }
public enum EarShape { Normal, Pixie, Elephant, StickOut }
public enum TorsoStyle { TShirt, LongSleeve, RolledSleeves, TankTop, Jacket, Hoodie, Polo, CheckShirt }
public enum LegsStyle { Trousers, Cargo, Shorts, TallBoots, Skirt, Jeans, Thobe }
public enum Footwear { Boots, Trainers }
public enum HatStyle { None, Helmet, Cap, Beret, Boonie, Beanie, Keffiyeh, Turban }
public enum EyewearStyle { None, Sunglasses, Goggles }
public enum BackpackStyle { None, Light, Rucksack, Hydration, Radio }
public enum VestStyle { None, ChestRig, PlateCarrier }
public enum BeltStyle { None, Plain, Pouches, Utility }

/// <summary>Everything that defines one generated character. Bindable and JSON-serialisable.</summary>
public sealed class CharacterSpec : INotifyPropertyChanged
{
    private string _name = "Character";
    private RigTarget _rig = RigTarget.ToonSoldiers;
    private Gender _gender = Gender.Male;
    private float _build = 0.5f;
    private float _headSize = 1.0f;
    private HeadShape _headShape = HeadShape.Standard;
    private EarShape _ears = EarShape.Normal;
    private HairStyle _hair = HairStyle.Short;
    private FacialHair _facialHair = FacialHair.None;
    private TorsoStyle _torso = TorsoStyle.TShirt;
    private LegsStyle _legs = LegsStyle.Trousers;
    private HatStyle _hat = HatStyle.None;
    private EyewearStyle _eyewear = EyewearStyle.None;
    private BackpackStyle _backpack = BackpackStyle.None;
    private VestStyle _vest = VestStyle.None;
    private BeltStyle _belt = BeltStyle.Plain;
    private bool _gloves, _kneePads, _elbowPads, _scarf, _flatShaded;
    private Camouflage _camouflage = Camouflage.None;
    private Edition _edition = Edition.Custom;
    private Footwear _footwear = Footwear.Boots;
    private FaceCover _faceCover = FaceCover.None;
    private CamoCoverage _camoCoverage = CamoCoverage.All;
    private float _grime, _fatigue, _stress;
    private int _textureSize = 2048;
    private Rgb _eyeColor = Rgb.FromHex("#5A4630");
    private Rgb _skinColor = Rgb.FromHex("#D9A274");
    private Rgb _hairColor = Rgb.FromHex("#4A3222");
    private Rgb _shirtColor = Rgb.FromHex("#6E6C48");
    private Rgb _trousersColor = Rgb.FromHex("#5E5A3E");
    private Rgb _bootsColor = Rgb.FromHex("#3A2F25");
    private Rgb _gearColor = Rgb.FromHex("#46483A");
    private Rgb _hatColor = Rgb.FromHex("#5C5B3C");
    private Rgb _accentColor = Rgb.FromHex("#5C4630");

    /// <summary>Used for the FBX object names and the default file name.</summary>
    public string Name { get => _name; set => Set(ref _name, value); }

    /// <summary>
    /// ToonSoldiers: chunky toon proportions on the Toon Soldiers pack skeleton (Bip001), so the pack's
    /// animations play on it. Mannequin: realistic proportions on the UE5 mannequin skeleton.
    /// </summary>
    public RigTarget Rig { get => _rig; set => Set(ref _rig, value); }

    public Gender Gender { get => _gender; set => Set(ref _gender, value); }

    /// <summary>0 = slim, 1 = heavy.</summary>
    public float Build { get => _build; set => Set(ref _build, Math.Clamp(value, 0f, 1f)); }

    /// <summary>Head scale relative to the mannequin's head (1 = realistic, larger = more stylised).</summary>
    public float HeadSize { get => _headSize; set => Set(ref _headSize, Math.Clamp(value, 0.9f, 1.4f)); }

    public HeadShape HeadShape { get => _headShape; set => Set(ref _headShape, value); }
    public EarShape Ears { get => _ears; set => Set(ref _ears, value); }
    public HairStyle Hair { get => _hair; set => Set(ref _hair, value); }
    public FacialHair FacialHair { get => _facialHair; set => Set(ref _facialHair, value); }
    public TorsoStyle Torso { get => _torso; set => Set(ref _torso, value); }
    public LegsStyle Legs { get => _legs; set => Set(ref _legs, value); }
    public HatStyle Hat { get => _hat; set => Set(ref _hat, value); }

    /// <summary>Balaclava or shemagh over the face; their colour (the shemagh's pattern) is <see cref="AccentColor"/>.</summary>
    public FaceCover FaceCover { get => _faceCover; set => Set(ref _faceCover, value); }

    /// <summary>Dirt and dust on skin, clothes and kit (0 = clean, 1 = filthy, mud up the legs).</summary>
    public float Grime { get => _grime; set => Set(ref _grime, Math.Clamp(value, 0f, 1f)); }

    /// <summary>Tired face: dark rings and bags under the eyes, red lids, sallow skin, hollow cheeks (0..1).</summary>
    public float Fatigue { get => _fatigue; set => Set(ref _fatigue, Math.Clamp(value, 0f, 1f)); }

    /// <summary>Strained face: furrowed brow, forehead lines, deep nose-to-mouth folds, sweat (0..1).</summary>
    public float Stress { get => _stress; set => Set(ref _stress, Math.Clamp(value, 0f, 1f)); }

    /// <summary>Which garments <see cref="Camouflage"/> is printed on.</summary>
    public CamoCoverage CamoCoverage { get => _camoCoverage; set => Set(ref _camoCoverage, value); }

    /// <summary>Boots or trainers; the colour is <see cref="BootsColor"/>.</summary>
    public Footwear Footwear { get => _footwear; set => Set(ref _footwear, value); }
    public EyewearStyle Eyewear { get => _eyewear; set => Set(ref _eyewear, value); }
    public BackpackStyle Backpack { get => _backpack; set => Set(ref _backpack, value); }
    public VestStyle Vest { get => _vest; set => Set(ref _vest, value); }
    public BeltStyle Belt { get => _belt; set => Set(ref _belt, value); }
    public bool Gloves { get => _gloves; set => Set(ref _gloves, value); }
    public bool KneePads { get => _kneePads; set => Set(ref _kneePads, value); }
    public bool ElbowPads { get => _elbowPads; set => Set(ref _elbowPads, value); }
    public bool Scarf { get => _scarf; set => Set(ref _scarf, value); }

    /// <summary>
    /// Uniform edition. Choosing one sets the clothing, hat, gear, boot and accent colours and a camouflage
    /// pattern (all still editable afterwards), and gives the camouflage that theatre's colours.
    /// Loading a preset or copying a spec restores the value without those side effects, so the saved
    /// colours and clothes win (see <see cref="EditionData"/> and <see cref="CopyFrom"/>).
    /// </summary>
    [JsonIgnore]
    public Edition Edition
    {
        get => _edition;
        set
        {
            if (_edition == value) return;
            _edition = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Edition)));
            if (EditionColors(value) is { } c)
            {
                ShirtColor = c.Shirt; TrousersColor = c.Trousers; HatColor = c.Hat; GearColor = c.Gear;
                BootsColor = c.Boots; AccentColor = c.Accent;
                if (value == Edition.Civilian) Civvies();
                else if (value == Edition.Militia) Irregulars();
                else if (Camouflage == Camouflage.None) Camouflage = Camouflage.Woodland;
            }
        }
    }

    /// <summary>Swaps military kit for civilian clothes; whatever is already civilian stays as it is.</summary>
    private void Civvies()
    {
        Camouflage = Camouflage.None;
        Vest = VestStyle.None;
        Footwear = Footwear.Trainers;
        Gloves = KneePads = ElbowPads = false;
        if (Hat is HatStyle.Helmet or HatStyle.Beret or HatStyle.Boonie) Hat = HatStyle.None;
        if (Legs is LegsStyle.Cargo or LegsStyle.TallBoots) Legs = LegsStyle.Jeans;
        if (Torso is TorsoStyle.Jacket or TorsoStyle.RolledSleeves) Torso = TorsoStyle.Hoodie;
        if (Belt is BeltStyle.Pouches or BeltStyle.Utility) Belt = BeltStyle.Plain;
        if (Backpack is BackpackStyle.Rucksack or BackpackStyle.Radio or BackpackStyle.Hydration) Backpack = BackpackStyle.Light;
    }

    /// <summary>Irregular forces: camo trousers under a plain top, a face covering, surplus webbing, boots.</summary>
    private void Irregulars()
    {
        if (Camouflage == Camouflage.None) Camouflage = Camouflage.Woodland;
        CamoCoverage = CamoCoverage.TrousersOnly;
        if (FaceCover == FaceCover.None) FaceCover = FaceCover.Shemagh;
        if (Vest == VestStyle.None) Vest = VestStyle.ChestRig;
        if (Hat is HatStyle.Boonie) Hat = HatStyle.None;
        Footwear = Footwear.Boots;
    }

    /// <summary>The edition as stored in presets: set without dressing the character.</summary>
    [JsonInclude, JsonPropertyName("Edition")]
    internal Edition EditionData
    {
        get => _edition;
        set
        {
            if (_edition == value) return;
            _edition = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Edition)));
        }
    }

    /// <summary>Default colours of an edition (null for <see cref="Edition.Custom"/>).</summary>
    public static (Rgb Shirt, Rgb Trousers, Rgb Hat, Rgb Gear, Rgb Boots, Rgb Accent)? EditionColors(Edition edition) => edition switch
    {
        Edition.Desert => (Rgb.FromHex("#B9A57B"), Rgb.FromHex("#AD9970"), Rgb.FromHex("#B4A27A"), Rgb.FromHex("#9C8660"), Rgb.FromHex("#8A6E4C"), Rgb.FromHex("#6E5638")),
        Edition.Snow => (Rgb.FromHex("#E8EAEC"), Rgb.FromHex("#E0E3E6"), Rgb.FromHex("#EEF0F1"), Rgb.FromHex("#C9CED2"), Rgb.FromHex("#3E3F42"), Rgb.FromHex("#9AA0A6")),
        Edition.Militia => (Rgb.FromHex("#2E2F33"), Rgb.FromHex("#6A6D70"), Rgb.FromHex("#1F2023"), Rgb.FromHex("#3A3A34"), Rgb.FromHex("#2A2622"), Rgb.FromHex("#1E1E20")),
        Edition.Civilian => (Rgb.FromHex("#A8433A"), Rgb.FromHex("#3E5C86"), Rgb.FromHex("#2E2E30"), Rgb.FromHex("#4A4038"), Rgb.FromHex("#E6E6E2"), Rgb.FromHex("#6A4A2A")),
        Edition.Jungle => (Rgb.FromHex("#4E5E34"), Rgb.FromHex("#46552F"), Rgb.FromHex("#4A5832"), Rgb.FromHex("#3B4430"), Rgb.FromHex("#2E2A22"), Rgb.FromHex("#4A3A28")),
        _ => null,
    };

    /// <summary>Pattern printed on the shirt, trousers and hat.</summary>
    public Camouflage Camouflage { get => _camouflage; set => Set(ref _camouflage, value); }

    /// <summary>Side length of the exported texture in pixels: 512, 1024 or 2048.</summary>
    public int TextureSize { get => _textureSize; set => Set(ref _textureSize, value <= 512 ? 512 : value <= 1024 ? 1024 : 2048); }

    /// <summary>Faceted normals instead of smoothed ones.</summary>
    public bool FlatShaded { get => _flatShaded; set => Set(ref _flatShaded, value); }

    public Rgb SkinColor { get => _skinColor; set => Set(ref _skinColor, value); }
    public Rgb HairColor { get => _hairColor; set => Set(ref _hairColor, value); }
    public Rgb EyeColor { get => _eyeColor; set => Set(ref _eyeColor, value); }
    public Rgb ShirtColor { get => _shirtColor; set => Set(ref _shirtColor, value); }
    public Rgb TrousersColor { get => _trousersColor; set => Set(ref _trousersColor, value); }
    public Rgb BootsColor { get => _bootsColor; set => Set(ref _bootsColor, value); }
    public Rgb GearColor { get => _gearColor; set => Set(ref _gearColor, value); }
    public Rgb HatColor { get => _hatColor; set => Set(ref _hatColor, value); }

    /// <summary>Belt, scarf and bedroll colour.</summary>
    public Rgb AccentColor { get => _accentColor; set => Set(ref _accentColor, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ---- presets -------------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static CharacterSpec FromJson(string json) =>
        JsonSerializer.Deserialize<CharacterSpec>(json, JsonOptions) ?? new CharacterSpec();

    public CharacterSpec Clone() => FromJson(ToJson());

    /// <summary>Copies every option from <paramref name="other"/>, raising change notifications.</summary>
    public void CopyFrom(CharacterSpec other)
    {
        foreach (var p in typeof(CharacterSpec).GetProperties())
            if (p is { CanRead: true, CanWrite: true } && p.Name != nameof(Edition))
                p.SetValue(this, p.GetValue(other));
        EditionData = other.Edition;   // the copied clothes and colours already reflect it
    }

    // ---- randomiser ----------------------------------------------------------------------

    private static readonly string[] SkinTones = ["#F3D1B0", "#E0AC82", "#C98E63", "#A56C43", "#7A4B2A", "#553320"];
    private static readonly string[] HairTones = ["#1E1A18", "#4A3222", "#6E4A2C", "#A8743C", "#D2B26E", "#8C8C8C", "#7A2E1C"];
    private static readonly string[] ClothTones =
    [
        "#6B7A4A", "#4E5A3A", "#8A7A55", "#B9A77C", "#3F4A5A", "#2C3440", "#5A5F66", "#8B8F94",
        "#D8D8D2", "#7A3B32", "#3E5F7A", "#2F2F33", "#6A5038", "#9A6A3A", "#3B5B45", "#A23C3C",
    ];
    private static readonly string[] EyeTones = ["#5A4630", "#3E2C1E", "#4F6B8A", "#5E7A52", "#7A7F86"];
    private static readonly string[] BootTones = ["#2E2620", "#1C1C1E", "#5C4630", "#7A6248", "#3A3A3C"];

    public static CharacterSpec Random(Random rng)
    {
        T Pick<T>() where T : struct, Enum
        {
            var values = Enum.GetValues<T>();
            return values[rng.Next(values.Length)];
        }
        Rgb Tone(string[] tones) => Rgb.FromHex(tones[rng.Next(tones.Length)]);
        bool Chance(double p) => rng.NextDouble() < p;

        var spec = new CharacterSpec
        {
            Gender = Pick<Gender>(),
            Build = (float)rng.NextDouble(),
            HeadSize = 0.97f + (float)rng.NextDouble() * 0.1f,
            HeadShape = Pick<HeadShape>(),
            Hair = Pick<HairStyle>(),
            Ears = Chance(0.3) ? Pick<EarShape>() : EarShape.Normal,
            Torso = Pick<TorsoStyle>(),
            Legs = Pick<LegsStyle>(),
            Hat = Chance(0.6) ? Pick<HatStyle>() : HatStyle.None,
            Eyewear = Chance(0.3) ? Pick<EyewearStyle>() : EyewearStyle.None,
            Backpack = Chance(0.5) ? Pick<BackpackStyle>() : BackpackStyle.None,
            Vest = Chance(0.4) ? Pick<VestStyle>() : VestStyle.None,
            Belt = Pick<BeltStyle>(),
            Gloves = Chance(0.3),
            KneePads = Chance(0.25),
            ElbowPads = Chance(0.2),
            Scarf = Chance(0.2),
            SkinColor = Tone(SkinTones),
            HairColor = Tone(HairTones),
            ShirtColor = Tone(ClothTones),
            TrousersColor = Tone(ClothTones),
            BootsColor = Tone(BootTones),
            GearColor = Tone(ClothTones),
            HatColor = Tone(ClothTones),
            AccentColor = Tone(BootTones),
            EyeColor = Tone(EyeTones),
            Camouflage = Chance(0.35) ? Pick<Camouflage>() : Camouflage.None,
        };
        spec.FacialHair = spec.Gender == Gender.Male && Chance(0.4) ? Pick<FacialHair>() : FacialHair.None;
        if (spec.Gender == Gender.Male && spec.Legs == LegsStyle.Skirt) spec.Legs = LegsStyle.Trousers;
        spec.Footwear = Chance(0.25) ? Footwear.Trainers : Footwear.Boots;
        return spec;
    }

    private static readonly string[] MilitiaTops = ["#2E2F33", "#1F2023", "#4A4A44", "#3B4434", "#5A4A3A", "#3E4A5A", "#6B6B66", "#7A6A4E"];
    private static readonly string[] MilitiaBottoms = ["#6A6D70", "#4E5A3A", "#5E5A3E", "#2E4466", "#26272B", "#6B5A44"];
    private static readonly string[] MilitiaWraps = ["#1E1E20", "#1E1E20", "#7A2A26", "#3A3428"];

    /// <summary>Insurgents: mismatched dark civvies and surplus kit, faces mostly covered.</summary>
    private static CharacterSpec RandomMilitia(Random rng, CharacterSpec spec)
    {
        T Of<T>(params T[] values) => values[rng.Next(values.Length)];
        Rgb Tone(string[] tones) => Rgb.FromHex(tones[rng.Next(tones.Length)]);
        bool Chance(double p) => rng.NextDouble() < p;
        spec.Edition = Edition.Militia;
        spec.Torso = Of(TorsoStyle.TShirt, TorsoStyle.TShirt, TorsoStyle.TankTop, TorsoStyle.LongSleeve, TorsoStyle.Hoodie, TorsoStyle.Jacket, TorsoStyle.CheckShirt);
        spec.Legs = Of(LegsStyle.Cargo, LegsStyle.Cargo, LegsStyle.Trousers, LegsStyle.Jeans);
        spec.FaceCover = Of(FaceCover.Balaclava, FaceCover.Shemagh, FaceCover.Shemagh, FaceCover.None);
        spec.Hat = spec.FaceCover == FaceCover.Balaclava ? (Chance(0.3) ? HatStyle.Helmet : HatStyle.None)
                 : Chance(0.5) ? HatStyle.None : Of(HatStyle.Beanie, HatStyle.Cap, HatStyle.Beret, HatStyle.Helmet);
        spec.Vest = Of(VestStyle.ChestRig, VestStyle.ChestRig, VestStyle.PlateCarrier, VestStyle.None);
        spec.Belt = Of(BeltStyle.Plain, BeltStyle.Pouches, BeltStyle.Utility);
        spec.Backpack = Chance(0.3) ? Of(BackpackStyle.Light, BackpackStyle.Rucksack) : BackpackStyle.None;
        spec.Eyewear = Chance(0.25) ? EyewearStyle.Sunglasses : EyewearStyle.None;
        spec.Gloves = Chance(0.4);
        spec.KneePads = spec.ElbowPads = false;
        spec.Scarf = spec.FaceCover != FaceCover.Balaclava && Chance(0.4);
        spec.Footwear = Chance(0.2) ? Footwear.Trainers : Footwear.Boots;
        spec.Camouflage = spec.Legs == LegsStyle.Jeans ? Camouflage.None : Of(Camouflage.Woodland, Camouflage.Digital, Camouflage.None);
        spec.CamoCoverage = Chance(0.8) ? CamoCoverage.TrousersOnly : CamoCoverage.All;
        spec.ShirtColor = Tone(MilitiaTops);
        spec.TrousersColor = Tone(MilitiaBottoms);
        spec.HatColor = Tone(MilitiaTops);
        spec.GearColor = Tone(MilitiaTops);
        spec.AccentColor = Tone(MilitiaWraps);
        spec.BootsColor = Rgb.FromHex(Of("#2A2622", "#1C1C1E", "#4A3A2A"));
        if (spec.Gender == Gender.Male && spec.FaceCover != FaceCover.Balaclava && Chance(0.55)) spec.FacialHair = FacialHair.BushyBeard;
        if (spec.Gender == Gender.Male && Chance(0.35))
        {
            // Traditional dress: an ankle-length thobe under the webbing, keffiyeh or turban.
            spec.Torso = TorsoStyle.LongSleeve;
            spec.Legs = LegsStyle.Thobe;
            spec.Camouflage = Camouflage.None;
            spec.Belt = BeltStyle.None;
            spec.Hat = Of(HatStyle.Keffiyeh, HatStyle.Keffiyeh, HatStyle.Turban);
            if (spec.FaceCover == FaceCover.Balaclava) spec.FaceCover = FaceCover.None;
            spec.ShirtColor = Rgb.FromHex(Of("#E8E4DA", "#D8CDB0", "#B9AE96", "#8E8A80", "#6A6A5A", "#4A4A40"));
            spec.HatColor = Rgb.FromHex(Of("#1E1E20", "#3A3428", "#E8E4DA", "#5A4A3A"));
            spec.AccentColor = Rgb.FromHex(Of("#7A2A26", "#1E1E20", "#1E1E20"));
            spec.FacialHair = FacialHair.BushyBeard;
        }
        if (spec.Gender == Gender.Female && spec.FaceCover == FaceCover.None) spec.FaceCover = FaceCover.Shemagh;
        spec.Grime = 0.25f + 0.6f * (float)rng.NextDouble();
        spec.Fatigue = 0.2f + 0.6f * (float)rng.NextDouble();
        spec.Stress = (float)rng.NextDouble() * 0.8f;
        return spec;
    }

    private static readonly string[] CivilianTops =
        ["#A8433A", "#D8D8D2", "#2F2F33", "#3E5F7A", "#6B8F4E", "#C9A23A", "#7A3B5A", "#4A6FA5", "#B86A3A", "#8B8F94"];
    private static readonly string[] Denim = ["#3E5C86", "#2E4466", "#52729C", "#26272B", "#5A5F66", "#6B5A44"];
    private static readonly string[] ShoeTones = ["#E6E6E2", "#1C1C1E", "#B03A32", "#3A4A6A", "#7A6248"];

    /// <summary>A random character dressed for an edition: civilian clothes and colours, or a uniform in that edition's colours.</summary>
    public static CharacterSpec Random(Random rng, Edition edition)
    {
        var spec = Random(rng);
        if (edition == Edition.Militia)
            return RandomMilitia(rng, spec);
        if (edition != Edition.Civilian)
        {
            if (edition != Edition.Custom) spec.Edition = edition;
            return spec;
        }

        T Of<T>(params T[] values) => values[rng.Next(values.Length)];
        Rgb Tone(string[] tones) => Rgb.FromHex(tones[rng.Next(tones.Length)]);
        spec.Edition = Edition.Civilian;
        spec.Torso = Of(TorsoStyle.TShirt, TorsoStyle.Hoodie, TorsoStyle.Polo, TorsoStyle.CheckShirt, TorsoStyle.LongSleeve, TorsoStyle.TankTop);
        spec.Legs = spec.Gender == Gender.Female
            ? Of(LegsStyle.Jeans, LegsStyle.Jeans, LegsStyle.Trousers, LegsStyle.Shorts, LegsStyle.Skirt)
            : Of(LegsStyle.Jeans, LegsStyle.Jeans, LegsStyle.Trousers, LegsStyle.Shorts);
        spec.Hat = rng.NextDouble() < 0.6 ? HatStyle.None : Of(HatStyle.Cap, HatStyle.Beanie);
        spec.Eyewear = rng.NextDouble() < 0.2 ? EyewearStyle.Sunglasses : EyewearStyle.None;
        spec.Belt = Of(BeltStyle.None, BeltStyle.Plain);
        spec.Backpack = rng.NextDouble() < 0.3 ? BackpackStyle.Light : BackpackStyle.None;
        spec.Scarf = rng.NextDouble() < 0.1;
        spec.Footwear = rng.NextDouble() < 0.8 ? Footwear.Trainers : Footwear.Boots;
        spec.ShirtColor = Tone(CivilianTops);
        spec.TrousersColor = spec.Legs == LegsStyle.Jeans ? Tone(Denim) : Tone(ClothTones);
        spec.HatColor = Tone(CivilianTops);
        spec.GearColor = Tone(ClothTones);
        spec.BootsColor = spec.Footwear == Footwear.Trainers ? Tone(ShoeTones) : Tone(BootTones);
        return spec;
    }
}
