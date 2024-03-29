# MaxQuant Param File Mod Extractor

This program parses a MaxQuant parameter file (XML-based) to look for and display the nodes
that define static and dynamic mods. It also looks for isobaric label nodes.

The output from this program can be used when adding a new MaxQuant parameter file to DMS using the Param File Entry Page.
* https://dms2.pnl.gov/param_file/create/3577

Alternatively, the program can be used to add/remove/replace parameters in a MaxQuant parameter file created by an older version of MaxQuant.

## Example Data

Example MaxQuant parameter files are in the Docs directory

Example output:
```XML
    <variableModifications>
        <string>Oxidation (M)</string>
        <string>Acetyl (Protein N-term)</string>
    </variableModifications>
    <isobaricLabels>
       <IsobaricLabelInfo>
          <internalLabel>TMT10plex-Lys126C</internalLabel>
          <terminalLabel>TMT10plex-Nter126C</terminalLabel>
       </IsobaricLabelInfo>
    </isobaricLabels>    
```

## Example Command line 

```
MaxQuantParamFileModExtractor.exe MaxQuant_Tryp_Stat_CysAlk_Dyn_MetOx_NTermAcet_20ppmParTol.xml

MaxQuantParamFileModExtractor.exe /I:MaxQuant*.xml

MaxQuantParamFileModExtractor.exe /I:MaxQuant*.xml [/Update]
```

## Command Line Syntax

The MaxQuantParamFileModExtractor is a console application, and must be run from the Windows command prompt.

```
MaxQuantParamFileModExtractor /I:InputFilePath [/Update]
```

Use `/I` to define the MaxQuant parameter file to examine (XML-based parameter file). 
* Wildcards are supported

Use `/Update` or `/U` to update the MaxQuant parameter file, adding, removing, or replacing parameters to make the parameter file compatible with the latest release of MaxQuant
* The original file will be replaced, after it is renamed to .xml.old
* If a .xml.old file already exists, the updated file will be named ParameterFileName.xml.new

## Contacts

Written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA) \
E-mail: matthew.monroe@pnnl.gov or proteomics@pnnl.gov \
Website: https://github.com/PNNL-Comp-Mass-Spec/ or https://www.pnnl.gov/integrative-omics

## License

Licensed under the 2-Clause BSD License; you may not use this file except
in compliance with the License.  You may obtain a copy of the License at
https://opensource.org/licenses/BSD-2-Clause
