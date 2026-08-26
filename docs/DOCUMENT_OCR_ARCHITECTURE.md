# Document and OCR Architecture

The document path is `immutable source → classification → native PDF text attempt → PaddleOCR only when required → structured extraction → validation → human review → register`.

Originals are copied to the managed repository by SHA-256 and never altered. A renamed duplicate resolves to the existing document. Derived extraction data records method, implementation version, text, confidence, page/bounding-box data where supplied, structured fields and review status.

`IDocumentTextExtractor` isolates the application from OCR technology. `NativePdfTextExtractor` detects a usable text layer without invoking OCR. `PaddleOcrProcessExtractor` calls a separately packaged, version-locked local helper without a shell, passes paths as discrete arguments, applies a timeout and consumes a bounded JSON result. Missing or failed OCR leaves workbook imports/reporting operational and routes the document to manual review.

Recommended production deployment is a signed x64 helper with CPU models stored below the application data directory. The installer should verify helper/model hashes before enabling OCR. GPU support is optional and must never become an application prerequisite. Official Windows deployment guidance: <https://www.paddleocr.ai/latest/en/version3.x/inference_deployment/local_inference/cpp/OCR_windows.html>.

OCR never writes trusted financial facts directly. Invoice number/date, GST, quantity, amount and supplier require validation and explicit verification before register use. The Source Inbox provides a human verify/reject queue; every decision records the Windows reviewer, timestamp and reason, and rejection quarantines the retained document.
