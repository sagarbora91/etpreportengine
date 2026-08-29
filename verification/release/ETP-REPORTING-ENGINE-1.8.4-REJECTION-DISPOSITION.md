# ETP Reporting Engine 1.8.4 rejection disposition

Date: 2026-08-29
Disposition: **REJECTED — PRESERVE, DO NOT PROMOTE**

The recorded 1.8.4 application, installer and offline package were never promoted and are permanently rejected.

## Reasons

- The shipped audit constraint omitted event literals emitted by shipped workflows, creating valid-operation audit failures and a mutation/audit atomicity risk.
- The committed 1.8.4 CycloneDX document identifies base commit `08e39c889fe2a13b09aa33ffa788df63f0b800fd`, a dirty source state and application hash `AA2EE80191F79402C340A2F3C8BBE240AB45B4A2C2EA6934026D5733122D0ABE`; the candidate provenance identifies source `8c8d57e37a26fcd8a9a145ac166b34ac952c8b4b`, a clean build and application hash `73C615C1EA9A943A74893CE8BE6C4CFDF28796B4BD806902A7EA3A5A014A2B37`.

## Preservation boundary

Do not delete or rewrite the 1.8.4 binaries, hashes, SBOM or provenance. Their inconsistency is itself historical evidence. No additional testing, signature or approval can convert those hashes into an acceptable release.

Source version 1.8.5 contains the corrective implementation, but no 1.8.5 binary, installer, SBOM, provenance, signature, tag or release exists yet. A future candidate must be built from clean committed source and pass all engineering and external gates under a new evidence set.
