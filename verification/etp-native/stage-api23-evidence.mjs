import { copyFileSync, mkdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here=path.dirname(fileURLToPath(import.meta.url));
const root=path.resolve(here,'../..');
const target=path.join(root,'android/app/src/androidTest/java/com/saagartraders/bcc/EtpNativeApi23EvidenceTest.java');
mkdirSync(path.dirname(target),{recursive:true});
copyFileSync(path.join(here,'EtpNativeApi23EvidenceTest.java'),target);
console.log('[etp-native-evidence] staged canonical API23 instrumentation source');
