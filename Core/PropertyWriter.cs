using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Interop.ComApi;

namespace NavisworksIfcExporter.Core
{
    /// <summary>
    /// Grava propriedades customizadas em elementos do Navisworks usando a COM API.
    /// Os dados ficam persistidos no NWD/NWF como abas extras visíveis no painel Properties.
    ///
    /// Limitação: propriedades nativas (Revit, AutoCAD, IFC) são somente-leitura.
    /// Esta classe cria/edita apenas abas CUSTOMIZADAS (Data Tools do Navisworks Manage).
    /// </summary>
    public class PropertyWriter
    {
        /// <summary>
        /// Grava (ou atualiza) uma propriedade em todos os <paramref name="items"/>.
        /// </summary>
        public void Write(
            IEnumerable<ModelItem> items,
            string tabDisplayName,
            string propertyName,
            string value)
        {
            if (string.IsNullOrWhiteSpace(tabDisplayName))
                throw new ArgumentException("Nome da aba não pode ser vazio.", nameof(tabDisplayName));
            if (string.IsNullOrWhiteSpace(propertyName))
                throw new ArgumentException("Nome da propriedade não pode ser vazio.", nameof(propertyName));

            var itemList = items.ToList();
            if (itemList.Count == 0) return;

            // Acessa o estado COM via ComApiBridge (mesmo padrão do GeometryExtractor).
            // ComApiBridge.State retorna o InwOpState — bridge entre a API gerenciada e a COM.
            dynamic comState = GetComState();

            // DataTools() retorna o InwOpDataToolsCDL — gestor de abas customizadas.
            dynamic dataTools;
            try
            {
                dataTools = comState.DataTools();
            }
            catch (Exception ex)
            {
                throw new NotSupportedException(
                    "Não foi possível acessar DataTools do Navisworks via COM. " +
                    "Verifique se está usando Navisworks Manage (não Freedom/Simulate).", ex);
            }

            dynamic tab   = FindOrCreateTab(dataTools, tabDisplayName);
            dynamic field = FindOrCreateField(tab, propertyName);

            // Monta a seleção COM e define o valor por elemento
            var collection = new ModelItemCollection();
            foreach (var item in itemList)
                collection.Add(item);

            var comSel = ComApiBridge.ToInwOpSelection(collection);

            foreach (InwOaPath3 path in comSel.Paths())
            {
                try
                {
                    SetFieldValue(path, tab, field, value);
                }
                catch (Exception ex)
                {
                    PluginLogger.Warn($"[PropertyWriter] Erro ao gravar em path: {ex.Message}");
                }
            }
        }

        // -----------------------------------------------------------------------

        private static dynamic GetComState()
        {
            // ComApiBridge.State é a propriedade estática que expõe o InwOpState.
            // Usamos reflexão para compatibilidade entre versões do Navisworks.
            var bridgeType = typeof(ComApiBridge);
            var stateProp  = bridgeType.GetProperty(
                "State",
                BindingFlags.Public | BindingFlags.Static);

            if (stateProp != null)
                return stateProp.GetValue(null)
                       ?? throw new InvalidOperationException("ComApiBridge.State retornou null.");

            // Fallback: tenta via método estático GetState() (versões mais antigas)
            var stateMethod = bridgeType.GetMethod(
                "GetState",
                BindingFlags.Public | BindingFlags.Static);

            if (stateMethod != null)
                return stateMethod.Invoke(null, null)
                       ?? throw new InvalidOperationException("ComApiBridge.GetState() retornou null.");

            throw new NotSupportedException(
                "ComApiBridge não expõe 'State' nem 'GetState'. " +
                "Verifique a versão da API do Navisworks instalada.");
        }

        private static dynamic FindOrCreateTab(dynamic dataTools, string name)
        {
            foreach (dynamic tab in dataTools.Tabs())
            {
                if (string.Equals((string)tab.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                    return tab;
            }
            return dataTools.AddTab(name);
        }

        private static dynamic FindOrCreateField(dynamic tab, string name)
        {
            foreach (dynamic field in tab.Fields())
            {
                if (string.Equals((string)field.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                    return field;
            }
            // nwEDataType: 1 = eDataString
            return tab.AddField(name, 1);
        }

        /// <summary>
        /// Grava múltiplas propriedades em múltiplos elementos em uma única sessão COM
        /// para minimizar overhead de aquisição de DataTools.
        /// </summary>
        public void WriteAll(
            string tabDisplayName,
            IEnumerable<(ModelItem Item, Dictionary<string, string> Props)> batch)
        {
            var list = batch.ToList();
            if (list.Count == 0) return;

            dynamic comState = GetComState();
            dynamic dataTools;
            try { dataTools = comState.DataTools(); }
            catch (Exception ex)
            {
                throw new NotSupportedException(
                    "Não foi possível acessar DataTools do Navisworks via COM. " +
                    "Verifique se está usando Navisworks Manage (não Freedom/Simulate).", ex);
            }

            dynamic tab = FindOrCreateTab(dataTools, tabDisplayName);
            var fieldCache = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var (item, props) in list)
            {
                var collection = new ModelItemCollection();
                collection.Add(item);
                var comSel = ComApiBridge.ToInwOpSelection(collection);

                foreach (InwOaPath3 path in comSel.Paths())
                {
                    foreach (var kv in props)
                    {
                        if (!fieldCache.TryGetValue(kv.Key, out var fieldObj))
                            fieldCache[kv.Key] = fieldObj = FindOrCreateField(tab, kv.Key);

                        try { SetFieldValue(path, tab, (dynamic)fieldObj, kv.Value); }
                        catch (Exception ex)
                        {
                            PluginLogger.Warn($"[PropertyWriter.WriteAll] Erro em '{kv.Key}': {ex.Message}");
                        }
                    }
                }
            }
        }

        private static void SetFieldValue(InwOaPath3 path, dynamic tab, dynamic field, string value)
        {
            // Obtém (ou cria) o DataItem associado ao elemento nesta aba
            dynamic dataItem = ((dynamic)path).DataItem(tab);
            dataItem.SetValue(field, value);
        }
    }
}
