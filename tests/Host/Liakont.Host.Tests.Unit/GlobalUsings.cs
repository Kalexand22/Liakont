// Helper RB6 partagé (AddBrowserTimeZoneStub) accessible sans using répété dans les tests de pages/composants migrés.
// SA1200 ne s'applique pas : un « global using » doit obligatoirement être au niveau fichier (hors namespace).
#pragma warning disable SA1200
global using Liakont.Host.Tests.Unit.Time;
#pragma warning restore SA1200
