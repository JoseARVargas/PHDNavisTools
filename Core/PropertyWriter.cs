using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Interop.ComApi;

namespace PHDNavisTools.Core
{
    /// <summary>
    /// Grava propriedades customizadas em elementos do Navisworks via COM API (InwGUIPropertyNode2).
    /// As propriedades ficam persistidas no NWD/NWF como abas extras visíveis no painel Properties.
    /// Apenas abas CUSTOMIZADAS (user-defined) podem ser criadas/editadas.
    /// Propriedades nativas (Revit, AutoCAD, IFC) são somente-leitura.
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

            var state = (InwOpState10)ComApiBridge.State;
            state.BeginEdit("PHD Write Properties");
            try
            {
                foreach (var item in itemList)
                    WriteToItem(state, item, tabDisplayName, propertyName, value);
            }
            finally
            {
                state.EndEdit();
            }
        }

        /// <summary>
        /// Grava múltiplas propriedades em múltiplos elementos em uma única sessão COM.
        /// </summary>
        public void WriteAll(
            string tabDisplayName,
            IEnumerable<(ModelItem Item, Dictionary<string, string> Props)> batch)
        {
            var list = batch.ToList();
            if (list.Count == 0) return;

            var state = (InwOpState10)ComApiBridge.State;
            state.BeginEdit("PHD Write Properties");
            try
            {
                foreach (var (item, props) in list)
                    foreach (var kv in props)
                    {
                        try
                        {
                            WriteToItem(state, item, tabDisplayName, kv.Key, kv.Value);
                        }
                        catch (Exception ex)
                        {
                            PluginLogger.Warn($"[PropertyWriter.WriteAll] Erro em '{kv.Key}': {ex.Message}");
                        }
                    }
            }
            finally
            {
                state.EndEdit();
            }
        }

        // -----------------------------------------------------------------------

        private static void WriteToItem(
            InwOpState10 state,
            ModelItem item,
            string tabDisplayName,
            string propertyName,
            string value)
        {
            var collection = new ModelItemCollection { item };
            var comSel = ComApiBridge.ToInwOpSelection(collection);

            // Use only the first path — multiple paths represent geometry fragments/instances
            // of the same element; writing to one node is sufficient and avoids COM conflicts.
            InwOaPath3? firstPath = null;
            foreach (object pathObj in comSel.Paths())
            {
                firstPath = pathObj as InwOaPath3;
                break;
            }
            if (firstPath == null) return;

            var guiNode = (InwGUIPropertyNode2)state.GetGUIPropertyNode(firstPath, false);
                var guiAttribs = guiNode.GUIAttributes();

                // Search for an existing user-defined tab with matching display name
                int existingIdx = -1;
                InwGUIAttribute2? existingAttr = null;
                int idx = 1;
                foreach (object attrObj in guiAttribs)
                {
                    if (attrObj is InwGUIAttribute2 ga && ga.UserDefined &&
                        string.Equals(ga.ClassUserName, tabDisplayName, StringComparison.OrdinalIgnoreCase))
                    {
                        existingIdx = idx;
                        existingAttr = ga;
                        break;
                    }
                    idx++;
                }

                // Build a new InwOaPropertyVec with the desired properties
                var propVec = (InwOaPropertyVec)state.ObjectFactory(
                    nwEObjectType.eObjectType_nwOaPropertyVec, null, null);
                var vecProps = (InwOaPropertyColl)propVec.Properties();

                string internalName;

                if (existingAttr != null)
                {
                    // Rebuild the property vec from scratch — never reuse existing COM objects
                    // across collections; Navisworks marks them invalid after the first Add.
                    internalName = existingAttr.name;
                    var existingProps = (InwOaPropertyColl)existingAttr.Properties();
                    bool found = false;
                    foreach (object propObj in existingProps)
                    {
                        if (propObj is InwOaProperty p)
                        {
                            string pUserName = p.UserName;
                            string pName     = p.name;
                            object pValue    = p.value;

                            if (!found &&
                                string.Equals(pUserName, propertyName, StringComparison.OrdinalIgnoreCase))
                            {
                                vecProps.Add(MakeProperty(state, pName, pUserName, value));
                                found = true;
                            }
                            else
                            {
                                // Fresh copy — do NOT add the original COM object to a new collection
                                vecProps.Add(MakeProperty(state, pName, pUserName,
                                    pValue?.ToString() ?? string.Empty));
                            }
                        }
                    }

                    if (!found)
                        vecProps.Add(MakeProperty(state, propertyName, propertyName, value));

                    guiNode.SetUserDefined(existingIdx, tabDisplayName, internalName, propVec);
                }
                else
                {
                    internalName = SanitizeInternalName(tabDisplayName);
                    vecProps.Add(MakeProperty(state, propertyName, propertyName, value));

                    // ndx = 0 appends a new user-defined attribute
                    guiNode.SetUserDefined(0, tabDisplayName, internalName, propVec);
                }
        }

        private static InwOaProperty MakeProperty(
            InwOpState10 state, string name, string userName, string value)
        {
            var prop = (InwOaProperty)state.ObjectFactory(
                nwEObjectType.eObjectType_nwOaProperty, null, null);
            prop.name     = name;
            prop.UserName = userName;
            prop.value    = value;
            return prop;
        }

        // COM internal names may not contain spaces — replace with underscore
        private static string SanitizeInternalName(string displayName) =>
            displayName.Replace(' ', '_');
    }
}
