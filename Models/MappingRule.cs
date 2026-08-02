using System.Runtime.Serialization;

namespace PHDNavisTools.Models
{
    [DataContract]
    public sealed class MappingRule
    {
        /// <summary>Categoria/Pset de origem (vazio = qualquer categoria).</summary>
        [DataMember] public string SourcePset { get; set; } = "";

        /// <summary>Nome da propriedade de origem (vazio = qualquer propriedade da categoria).</summary>
        [DataMember] public string SourceProperty { get; set; } = "";

        /// <summary>Categoria/Pset de destino no IFC (vazio = manter original).</summary>
        [DataMember] public string TargetPset { get; set; } = "";

        /// <summary>Nome da propriedade no IFC (vazio = manter original).</summary>
        [DataMember] public string TargetProperty { get; set; } = "";

        /// <summary>Se verdadeiro, a propriedade é excluída da exportação.</summary>
        [DataMember] public bool Exclude { get; set; } = false;
    }
}
