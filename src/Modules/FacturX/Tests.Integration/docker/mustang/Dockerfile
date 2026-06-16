# Image de validation Factur-X — Mustangproject CLI (ADR-0023 §4, FX04).
# Mustangproject n'a PAS d'image Docker officielle (ZUGFeRD/mustangproject#514) : on embarque le jar
# CLI à une version FIGÉE sur une JRE générique. Outillage CI externe (Apache-2.0) appelé en conteneur
# (« mere aggregation ») — aucune contrainte de licence sur le produit Liakont. JRE embarquée : aucun
# Java requis sur le runner. Le jar est téléchargé au BUILD (réseau au build, pas au runtime du test).
FROM eclipse-temurin:21-jre

# Version figée du Mustang CLI (release GitHub ZUGFeRD/mustangproject). À bumper explicitement.
ARG MUSTANG_VERSION=2.24.0
ADD https://github.com/ZUGFeRD/mustangproject/releases/download/core-${MUSTANG_VERSION}/Mustang-CLI-${MUSTANG_VERSION}.jar /opt/mustang/mustang-cli.jar

# Répertoire de travail inscriptible (Mustang écrit un dossier logs/ relatif au cwd).
WORKDIR /work

# ENTRYPOINT one-shot : `--action validate --source <fichier>`. exit 0 = valide, != 0 = invalide/erreur.
ENTRYPOINT ["java", "-jar", "/opt/mustang/mustang-cli.jar"]
