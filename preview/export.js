'use strict';
// Minimal OOXML/ZIP writer for the static preview; no network service or personal rows.
(() => {
  const xml = s => String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&apos;'}[c]));
  const ns = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main';
  const rel = 'http://schemas.openxmlformats.org/package/2006/relationships';
  const office = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships';
  const enc = new TextEncoder();
  const crc = bytes => { let c=0xffffffff;for(const b of bytes){c^=b;for(let i=0;i<8;i++)c=(c>>>1)^((c&1)?0xedb88320:0);}return(c^0xffffffff)>>>0; };
  function zip(files) {
    const local=[], central=[];let offset=0;
    for(const [path,text] of Object.entries(files)) {
      const name=enc.encode(path),data=enc.encode(text),sum=crc(data),head=new Uint8Array(30+name.length),h=new DataView(head.buffer);
      h.setUint32(0,0x04034b50,true);h.setUint16(4,20,true);h.setUint16(6,0x800,true);h.setUint32(14,sum,true);h.setUint32(18,data.length,true);h.setUint32(22,data.length,true);h.setUint16(26,name.length,true);head.set(name,30);local.push(head,data);
      const entry=new Uint8Array(46+name.length),e=new DataView(entry.buffer);e.setUint32(0,0x02014b50,true);e.setUint16(4,20,true);e.setUint16(6,20,true);e.setUint16(8,0x800,true);e.setUint32(16,sum,true);e.setUint32(20,data.length,true);e.setUint32(24,data.length,true);e.setUint16(28,name.length,true);e.setUint32(42,offset,true);entry.set(name,46);central.push(entry);offset+=head.length+data.length;
    }
    const end=new Uint8Array(22),v=new DataView(end.buffer);v.setUint32(0,0x06054b50,true);v.setUint16(8,central.length,true);v.setUint16(10,central.length,true);v.setUint32(12,central.reduce((n,b)=>n+b.length,0),true);v.setUint32(16,offset,true);
    return new Blob([...local,...central,end],{type:'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'});
  }
  window.demoExcel = report => {
    const unavailable='Für diese Auswahl nicht vollständig verfügbar';
    const summary=[['NEUER KIDS · Demo-Auswertung'],['Ausschließlich erfundene Beispieldaten'],...report.filterLabels.map(x=>[x]),['Kennzahl','Auswahl','Vergleich','Veränderung absolut','Veränderung %']];
    for(const [key,label] of [['visits','Besuche'],['children',report.crossSite?'Kinder (Standortzählungen)':'Unterschiedliche Kinder'],['average','Besuche je Kalendertag']]){const c=report.current.metrics[key],p=report.comparison?.metrics[key];summary.push([label,c??unavailable,p??(report.comparison?unavailable:'Ohne Vergleich'),c!=null&&p!=null?c-p:'',c!=null&&p?Math.round((c-p)/p*10000)/100:'']);}
    summary.push(['Berücksichtigte Kalendertage',report.current.metrics.calendarDays,report.comparison?.metrics.calendarDays??'']);
    const sheets=[['Überblick',summary]];
    for(const [slice,prefix] of [[report.current,'Auswahl'],[report.comparison,'Vergleich']])if(slice){const rows=[['Bereich','Kategorie','Besuche aus Einzelanwesenheiten']];for(const [key,label] of [['timeline','Verlauf'],['weekdays','Wochentag'],['ages','Alter'],['genders','Geschlecht'],['nationalities','Nationalität'],['sites','Standort']])for(const b of slice[key])rows.push([label,b.label,b.value]);sheets.push([prefix+' Diagramme',rows]);sheets.push([prefix+' Archiv',[['Standort','Monat','Archivbesuche','Archivprofile im Monat'],...slice.archive.map(a=>[a.site,a.month,a.visits,a.profiles])]]);if(slice.notice)summary.push([slice.notice]);}
    const files={};
    files['[Content_Types].xml']=`<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>${sheets.map((_,i)=>`<Override PartName="/xl/worksheets/sheet${i+1}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>`).join('')}</Types>`;
    files['_rels/.rels']=`<Relationships xmlns="${rel}"><Relationship Id="rId1" Type="${office}/officeDocument" Target="xl/workbook.xml"/></Relationships>`;
    files['xl/workbook.xml']=`<workbook xmlns="${ns}" xmlns:r="${office}"><sheets>${sheets.map(([name],i)=>`<sheet name="${xml(name)}" sheetId="${i+1}" r:id="rId${i+1}"/>`).join('')}</sheets></workbook>`;
    files['xl/_rels/workbook.xml.rels']=`<Relationships xmlns="${rel}">${sheets.map((_,i)=>`<Relationship Id="rId${i+1}" Type="${office}/worksheet" Target="worksheets/sheet${i+1}.xml"/>`).join('')}</Relationships>`;
    sheets.forEach(([_,rows],i)=>{files[`xl/worksheets/sheet${i+1}.xml`]=`<worksheet xmlns="${ns}"><cols><col min="1" max="1" width="55" customWidth="1"/><col min="2" max="5" width="24" customWidth="1"/></cols><sheetData>${rows.map((r,j)=>`<row r="${j+1}">${r.map((c,k)=>`<c r="${String.fromCharCode(65+k)}${j+1}"${typeof c==='number'?'':' t="inlineStr"'}>${typeof c==='number'?`<v>${c}</v>`:`<is><t xml:space="preserve">${xml(c)}</t></is>`}</c>`).join('')}</row>`).join('')}</sheetData></worksheet>`;});
    return zip(files);
  };
})();
