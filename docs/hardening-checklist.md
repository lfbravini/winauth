# Hardening checklist

- [ ] No Base32 / otpauth / QR shown in Console, Client, or CLI stdout
- [ ] Admin password via env/stdin only for CLI
- [ ] JWT kept in memory; not written to disk
- [ ] Local credential uses DPAPI CurrentUser + machine name check
- [ ] One managed TOTP per user; replace destroys prior serials
- [ ] CLI refuses replace without `--replace-token`
- [ ] Post-bind `/validate/check` enabled by default
- [ ] Horizon media not committed to public git
- [ ] No live admin credentials in repo or docs samples
- [ ] Client has no Show Secret / Export / Add personal token UI
