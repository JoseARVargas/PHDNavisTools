using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Interop.ComApi;

namespace PHDNavisTools.Core
{
    public class EraseResult
    {
        public int ElementsAffected { get; set; }
        public int ElementsSkipped  { get; set; }
        public List<string> Warnings { get; set; } = new();
    }

    public class PropertyEraser
    {
        public event EventHandler<string>? ProgressChanged;

        /// <summary>
        /// Remove a aba inteira (e todas as suas propriedades) dos elementos fornecidos.
        /// </summary>
        public EraseResult DeleteTab(IEnumerable<ModelItem> items, string tabDisplayName)
        {
            var list   = items.ToList();
            var result = new EraseResult();
            if (list.Count == 0) return result;

            var state = (InwOpState10)ComApiBridge.State;
            state.BeginEdit("PHD Clear Properties");
            try
            {
                foreach (var item in list)
                {
                    try
                    {
                        if (EraseTab(state, item, tabDisplayName))
                            result.ElementsAffected++;
                        else
                            result.ElementsSkipped++;
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Erro em elemento: {ex.Message}");
                        result.ElementsSkipped++;
                    }
                }
            }
            finally { state.EndEdit(); }

            return result;
        }

        /// <summary>
        /// Remove propriedades específicas de uma aba.
        /// Se a aba ficar vazia após a remoção, ela também é removida.
        /// </summary>
        public EraseResult DeleteProperties(
            IEnumerable<ModelItem> items,
            string tabDisplayName,
            IEnumerable<string> propertyNames)
        {
            var list         = items.ToList();
            var propsToErase = new HashSet<string>(propertyNames, StringComparer.OrdinalIgnoreCase);
            var result       = new EraseResult();
            if (list.Count == 0 || propsToErase.Count == 0) return result;

            var state = (InwOpState10)ComApiBridge.State;
            state.BeginEdit("PHD Clear Properties");
            try
            {
                foreach (var item in list)
                {
                    try
                    {
                        if (EraseProperties(state, item, tabDisplayName, propsToErase))
                            result.ElementsAffected++;
                        else
                            result.ElementsSkipped++;
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Erro em elemento: {ex.Message}");
                        result.ElementsSkipped++;
                    }
                }
            }
            finally { state.EndEdit(); }

            return result;
        }

        // -----------------------------------------------------------------------

        private static bool EraseTab(InwOpState10 state, ModelItem item, string tabDisplayName)
        {
            var path = GetFirstPath(item);
            if (path == null) return false;

            var guiNode = (InwGUIPropertyNode2)state.GetGUIPropertyNode(path, false);
            int idx = 1;
            foreach (object attrObj in guiNode.GUIAttributes())
            {
                if (attrObj is InwGUIAttribute2 ga && ga.UserDefined &&
                    string.Equals(ga.ClassUserName, tabDisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    // Vetor vazio → Navisworks remove a aba do elemento
                    var emptyVec = (InwOaPropertyVec)state.ObjectFactory(
                        nwEObjectType.eObjectType_nwOaPropertyVec, null, null);
                    guiNode.SetUserDefined(idx, tabDisplayName, ga.name, emptyVec);
                    return true;
                }
                idx++;
            }
            return false; // aba não existe neste elemento
        }

        private static bool EraseProperties(
            InwOpState10      state,
            ModelItem         item,
            string            tabDisplayName,
            HashSet<string>   propsToErase)
        {
            var path = GetFirstPath(item);
            if (path == null) return false;

            var guiNode = (InwGUIPropertyNode2)state.GetGUIPropertyNode(path, false);
            int idx = 1;
            foreach (object attrObj in guiNode.GUIAttributes())
            {
                if (attrObj is InwGUIAttribute2 ga && ga.UserDefined &&
                    string.Equals(ga.ClassUserName, tabDisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    var newVec   = (InwOaPropertyVec)state.ObjectFactory(
                        nwEObjectType.eObjectType_nwOaPropertyVec, null, null);
                    var vecProps = (InwOaPropertyColl)newVec.Properties();

                    bool removed = false;
                    foreach (object propObj in (InwOaPropertyColl)ga.Properties())
                    {
                        if (propObj is InwOaProperty p)
                        {
                            if (propsToErase.Contains(p.UserName))
                            {
                                removed = true;
                                continue; // descarta esta propriedade
                            }
                            // Reconstrói cópia limpa (nunca reutilizar objeto COM original)
                            vecProps.Add(MakeProperty(state, p.name, p.UserName,
                                p.value?.ToString() ?? string.Empty));
                        }
                    }

                    if (removed)
                        guiNode.SetUserDefined(idx, tabDisplayName, ga.name, newVec);

                    return removed;
                }
                idx++;
            }
            return false;
        }

        private static InwOaPath3? GetFirstPath(ModelItem item)
        {
            var col    = new ModelItemCollection { item };
            var comSel = ComApiBridge.ToInwOpSelection(col);
            foreach (object o in comSel.Paths())
                return o as InwOaPath3;
            return null;
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

        private void Report(string msg) => ProgressChanged?.Invoke(this, msg);
    }
}
