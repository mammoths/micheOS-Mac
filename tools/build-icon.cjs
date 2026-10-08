const fs = require('fs');
const path = require('path');
const sharp = require('sharp');
const m = 'M 4,147 L 4,76 C 4,53 17,42 37,42 C 51,42 60,47 66,56 C 74,46 85,41 99,41 C 128,41 146,58 146,85 L 146,146 Q 146,163 127,163 Q 108,163 108,146 L 108,89 Q 108,77 99,77 Q 89,77 89,89 L 89,146 Q 89,163 71,163 Q 52,163 52,146 L 52,89 Q 52,77 43,77 Q 34,77 34,89 L 34,147 Q 34,163 19,163 Q 4,163 4,147 Z';
const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 1024 1024"><defs><linearGradient id="bg" x2=".3" y2="1"><stop stop-color="#62465f"/><stop offset="1" stop-color="#241B2D"/></linearGradient><linearGradient id="m" x2="0" y2="1"><stop stop-color="#F8ECDD"/><stop offset="1" stop-color="#E6C89E"/></linearGradient><filter id="shadow" x="-30%" y="-30%" width="160%" height="170%"><feGaussianBlur stdDeviation="12"/></filter></defs><rect x="82" y="99" width="860" height="860" rx="190" fill="#171120" opacity=".35" filter="url(#shadow)"/><rect x="82" y="82" width="860" height="860" rx="190" fill="url(#bg)" stroke="#DFA6AD" stroke-opacity=".25" stroke-width="3"/><g transform="translate(228 118) scale(3.8)"><path d="${m}" fill="#1B1624" transform="translate(0 5)" opacity=".45"/><path d="${m}" fill="url(#m)"/></g></svg>`;
(async () => {
  const target = path.join(__dirname, '../assets/Miche.iconset'); fs.mkdirSync(target,{recursive:true});
  fs.writeFileSync(path.join(__dirname,'../assets/Miche.svg'),svg);
  for (const size of [16,32,128,256,512]) for (const scale of [1,2]) {
    await sharp(Buffer.from(svg)).resize(size*scale,size*scale).png().toFile(path.join(target,`icon_${size}x${size}${scale===2?'@2x':''}.png`));
  }
})().catch(e=>{console.error(e);process.exit(1)});
