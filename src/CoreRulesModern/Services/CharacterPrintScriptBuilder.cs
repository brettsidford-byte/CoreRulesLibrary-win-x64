using System.Text.Json;
using CoreRulesModern.Models;

namespace CoreRulesModern.Services;

public static class CharacterPrintScriptBuilder
{
    public const string ReadSectionHeadingsScript =
        "Array.from(document.querySelectorAll('body>table')).map((section,index)=>" +
        "section.querySelector(\"table[border='1'] font:first-child strong\")?.textContent?.trim()||`Section ${index+1}`)";

    public static string CreateApplyOptionsScript(CharacterPrintOptions options)
    {
        var encoded = JsonSerializer.Serialize(options);
        return "(()=>{const options=" + encoded + ";" +
               "document.getElementById('core-rules-print-options')?.remove();" +
               "const style=document.createElement('style');style.id='core-rules-print-options';" +
               "const orientation=options.Landscape?'landscape':'portrait';" +
               "let css=`@media print{@page{size:${options.PaperSize} ${orientation};margin:${options.MarginMm}mm;}" +
               "body>table.cr-user-print-break{break-before:page!important;page-break-before:always!important;}`;" +
               "css+=options.KeepSectionsTogether?'body>table{break-inside:avoid-page!important;page-break-inside:avoid!important;}':'body>table{break-inside:auto!important;page-break-inside:auto!important;}';" +
               "if(!options.PrintBackgrounds)css+='html,body,body *{background-image:none!important;box-shadow:none!important;}';" +
               "style.textContent=css+'}';(document.head||document.documentElement).appendChild(style);" +
               "const sections=Array.from(document.querySelectorAll('body>table'));" +
               "sections.forEach(s=>s.classList.remove('cr-user-print-break'));" +
               "if(options.SectionsPerPage>0){sections.forEach((s,i)=>{if(i>0&&i%options.SectionsPerPage===0)s.classList.add('cr-user-print-break');});}" +
               "for(let i=0;i<sections.length-1;i++){const heading=sections[i].querySelector(\"table[border='1'] font:first-child strong\")?.textContent?.trim()||`Section ${i+1}`;" +
               "if(options.BreakAfterSections.includes(heading))sections[i+1].classList.add('cr-user-print-break');}" +
               "})()";
    }
}
