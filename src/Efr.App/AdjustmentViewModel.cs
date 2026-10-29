using System.ComponentModel;
using System.Runtime.CompilerServices;
using Efr.Core;

namespace Efr.App;

public sealed class AdjustmentViewModel(Adjustment item) : INotifyPropertyChanged
{
    public Adjustment Item { get; } = item;
    private bool selected;
    private Detection detection = new(State.Unknown, "Aguardando consulta.");
    public bool Selected { get => selected; set { selected = value; Changed(); } }
    public string Detail => detection.Detail;
    public bool HasGuide => Item.GuideUrl != null;
    public bool NeedsChange => detection.State == State.NotApplied;
    public bool CanSelect => Item.Kind != OperationKind.Guide && detection.State is State.Applied or State.NotApplied;
    public string StateText => detection.State switch { State.Applied => "APLICADO", State.NotApplied => "NÃO APLICADO", State.Unavailable => "INDISPONÍVEL", _ => "DESCONHECIDO" };
    public void SetDetection(Detection value)
    {
        detection = value;
        if (!CanSelect) Selected = false;
        Changed(nameof(Detail)); Changed(nameof(StateText)); Changed(nameof(CanSelect));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
