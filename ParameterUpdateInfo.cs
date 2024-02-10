using System.Collections.Generic;

namespace MaxQuantParamFileModExtractor
{
    internal class ParameterUpdateInfo
    {
        public enum UpdateAction
        {
            AppendParameters = 0,
            Delete = 1,
            Replace = 2,
            UpdateValue = 3
        }

        /// <summary>
        /// Update action
        /// </summary>
        public UpdateAction Action { get; private set; }

        /// <summary>
        /// When appending new parameters, if this is true, append the parameters after the closing tag of TagName
        /// </summary>
        public bool AppendAfterClosingTag { get; set; } = true;

        /// <summary>
        /// New value to use when replacing the value for TagName
        /// </summary>
        public string NewValue { get; set; }

        /// <summary>
        /// Old value to look for when replacing the value for TagName
        /// </summary>
        /// <remarks>If this is an empty string, always replace the value; otherwise, only replace if the current value contains this text</remarks>
        public string OldValue { get; set; }

        public List<string> ParametersToAdd { get; set; }

        /// <summary>
        /// XML tag name, e.g. "&lt;intensityThresholdMs1" or "&lt;/diaPeptidePaths>"
        /// </summary>
        public string TagName { get; private set; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="tagName">XML tag name, e.g. "&lt;intensityThresholdMs1>" or "&lt;/diaPeptidePaths>" </param>
        /// <param name="action">Update action</param>
        public ParameterUpdateInfo(string tagName, UpdateAction action)
        {
            TagName = tagName;
            Action = action;

            NewValue = string.Empty;
            OldValue = string.Empty;

            ParametersToAdd = new List<string>();
        }
    }
}
