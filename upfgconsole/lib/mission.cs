using System.Text.Json;

public class Stage : ICloneable
{
    public int Id { get; set; }
    public int Mode { get; set; }
    public double GLim { get; set; }
    public double MassTotal { get; set; }
    public double MassDry { get; set; }
    public double Thrust { get; set; }
    public double Isp { get; set; }

    public object Clone()
    {
        return this.MemberwiseClone();
    }

}

public class GuidanceConfig
{
    public string program { get; set; }
    public float dt { get; set; }
}

public class Mission
{
    public Dictionary<string, float> Orbit { get; set; } = new Dictionary<string, float> { };
    public GuidanceConfig Guidance { get; set; } = new ();
    public Dictionary<string, float> Simulator { get; set; } = new Dictionary<string, float> { };
    public List<Stage> StageList { get; set; } = new List<Stage>();

    public static Mission Load(string filepath)
    {
        string json = File.ReadAllText(filepath);
        Mission? mission = JsonSerializer.Deserialize<Mission>(json);
        if (mission == null)
            throw new Exception($"Failed to deserialize mission file: {filepath}");
        return mission;
    }
}
