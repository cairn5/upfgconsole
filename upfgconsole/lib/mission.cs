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

public class MissionConfig
{
    public Dictionary<string, float> Orbit { get; set; } = new Dictionary<string, float> { };
    public List<Stage> StageList { get; set; } = new List<Stage>();
}
