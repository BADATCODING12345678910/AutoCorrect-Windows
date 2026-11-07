# -*- coding: utf-8 -*-
"""One-shot expander for language-pack _src (0.2.22 / pack 1.1.0)."""
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "website" / "language-packs" / "_src"


def write_pack(code: str, name: str, curated: list[tuple[str, str]], wordlist: list[str]) -> None:
    d = ROOT / code
    d.mkdir(parents=True, exist_ok=True)

    lines = [
        f"# AutoCorrect language pack: {name}",
        "# Format: typo<TAB>correction (one per line). High-confidence pairs only.",
    ]
    seen: set[str] = set()
    for trig, repl in curated:
        trig, repl = trig.strip(), repl.strip()
        if not trig or not repl or trig == repl:
            continue
        key = trig.casefold()
        if key in seen:
            continue
        seen.add(key)
        lines.append(f"{trig}\t{repl}")

    wl: list[str] = []
    wl_seen: set[str] = set()
    for w in wordlist:
        t = "".join(w.split()).strip() if False else w.strip()
        if not t or t.startswith("#"):
            continue
        k = t.casefold()
        if k in wl_seen:
            continue
        wl_seen.add(k)
        wl.append(t)
    for line in lines:
        if line.startswith("#") or "\t" not in line:
            continue
        repl = line.split("\t", 1)[1]
        k = repl.casefold()
        if k not in wl_seen:
            wl_seen.add(k)
            wl.append(repl)

    (d / "AutocorrectCurated.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    (d / "wordlist.txt").write_text("\n".join(wl) + "\n", encoding="utf-8")
    (d / "pack.json").write_text(
        json.dumps({"packVersion": "1.1.0", "code": code, "name": name}, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    pairs = sum(1 for x in lines if x and not x.startswith("#"))
    print(f"{code:<8} curated={pairs:3d}  wordlist={len(wl):4d}")


def pairs(*items: str) -> list[tuple[str, str]]:
    out: list[tuple[str, str]] = []
    for item in items:
        if "\t" not in item:
            continue
        a, b = item.split("\t", 1)
        out.append((a, b))
    return out


# ── Tier A ──────────────────────────────────────────────────────────────────

write_pack(
    "en-GB",
    "English (United Kingdom)",
    pairs(
        "colur\tcolour", "colourr\tcolour", "collour\tcolour", "color\tcolour",
        "organiza\torganise", "organiz\torganise", "organisee\torganise", "organize\torganise",
        "favou\tfavour", "favourr\tfavour", "favor\tfavour", "favror\tfavour",
        "centree\tcentre", "centerr\tcentre", "center\tcentre",
        "defenc\tdefence", "defencee\tdefence", "defense\tdefence",
        "travellingg\ttravelling", "traveling\ttravelling",
        "realis\trealise", "realisee\trealise", "realize\trealise",
        "licenc\tlicence", "licencee\tlicence", "license\tlicence",
        "behviour\tbehaviour", "behavour\tbehaviour", "behavior\tbehaviour", "behavoir\tbehaviour",
        "honor\thonour", "honuor\thonour",
        "neighbor\tneighbour", "nieghbour\tneighbour", "neigbour\tneighbour",
        "rumor\trumour", "rummor\trumour",
        "labor\tlabour", "labout\tlabour",
        "flavor\tflavour", "flavou\tflavour",
        "harbor\tharbour",
        "theater\ttheatre", "theatere\ttheatre",
        "meter\tmetre", "liter\tlitre",
        "analyze\tanalyse", "analise\tanalyse",
        "apologize\tapologise", "apoligise\tapologise",
        "recognize\trecognise", "recgonise\trecognise",
        "summarize\tsummarise", "specialize\tspecialise",
        "catalog\tcatalogue", "dialog\tdialogue",
        "program\tprogramme", "programne\tprogramme",
        "canceled\tcancelled", "canceld\tcancelled",
        "modeling\tmodelling", "traveler\ttraveller",
        "jewelry\tjewellery", "jewelery\tjewellery",
        "gray\tgrey", "tire\ttyre", "curb\tkerb",
        "practice\tpractise", "check\tcheque",
        "aluminum\taluminium", "airplane\taeroplane", "maneuver\tmanoeuvre",
        "teh\tthe", "hte\tthe", "adn\tand", "nad\tand", "fro\tfor", "jsut\tjust",
        "becuase\tbecause", "becaus\tbecause", "woudl\twould", "shoudl\tshould", "coudl\tcould",
        "lenght\tlength", "recieve\treceive", "seperate\tseparate", "occured\toccurred",
        "accomodate\taccommodate", "definately\tdefinitely", "goverment\tgovernment",
        "enviroment\tenvironment", "tommorow\ttomorrow", "tommorrow\ttomorrow",
        "wich\twhich", "whcih\twhich", "thier\ttheir", "theri\ttheir",
        "untill\tuntil", "alot\ta lot", "aswell\tas well",
        "necesary\tnecessary", "neccessary\tnecessary", "occassion\toccasion",
        "sucess\tsuccess", "succesful\tsuccessful", "succesfully\tsuccessfully",
        "recomend\trecommend", "reccomend\trecommend", "adress\taddress",
        "begining\tbeginning", "comming\tcoming", "writting\twriting",
        "buisness\tbusiness", "bussiness\tbusiness", "calender\tcalendar",
        "catagory\tcategory", "commitee\tcommittee", "concious\tconscious",
        "curiousity\tcuriosity", "dependant\tdependent", "dissapear\tdisappear",
        "embarass\tembarrass", "existance\texistence", "experiance\texperience",
        "familar\tfamiliar", "finaly\tfinally", "foriegn\tforeign",
        "fourty\tforty", "freind\tfriend", "happend\thappened",
        "immediatly\timmediately", "independant\tindependent",
        "knowlege\tknowledge", "liason\tliaison", "libary\tlibrary",
        "maintainance\tmaintenance", "millenium\tmillennium", "mispell\tmisspell",
        "noticable\tnoticeable", "occassionally\toccasionally", "oppurtunity\topportunity",
        "persistant\tpersistent", "posession\tpossession", "prefered\tpreferred",
        "priviledge\tprivilege", "probaly\tprobably", "proffessional\tprofessional",
        "publically\tpublicly", "realy\treally", "refered\treferred",
        "religous\treligious", "remeber\tremember", "rythm\trhythm",
        "seperately\tseparately", "sieze\tseize", "similiar\tsimilar",
        "suprise\tsurprise", "tatoo\ttattoo", "tendancy\ttendency",
        "truely\ttruly", "unfortunatly\tunfortunately", "usefull\tuseful",
        "usualy\tusually", "vaccuum\tvacuum", "wether\twhether", "whereever\twherever",
    ),
    [
        "the", "and", "for", "with", "that", "this", "from", "have", "been", "were", "which", "their",
        "there", "would", "could", "should", "about", "because", "people", "through", "before", "between",
        "during", "without", "another", "however", "although", "therefore", "meanwhile", "government",
        "organisation", "organisations", "colour", "colours", "favourite", "favourites", "centre", "centres",
        "defence", "licence", "behaviour", "honour", "neighbour", "rumour", "labour", "flavour", "harbour",
        "theatre", "metre", "litre", "analyse", "apologise", "recognise", "summarise", "specialise",
        "catalogue", "dialogue", "programme", "cancelled", "modelling", "traveller", "travelling",
        "jewellery", "grey", "tyre", "kerb", "practise", "cheque", "aluminium", "aeroplane", "manoeuvre",
        "realise", "realised", "organised", "recognised", "specialised", "catalogues", "programmes",
        "favourable", "endeavour", "enrolment", "sceptical", "skilful", "necessary", "accommodate",
        "definitely", "environment", "tomorrow", "receive", "separate", "occurred", "success", "recommend",
        "address", "beginning", "business", "calendar", "category", "committee", "conscious", "curiosity",
        "dependent", "disappear", "embarrass", "existence", "experience", "familiar", "finally", "foreign",
        "friend", "happened", "immediately", "independent", "knowledge", "liaison", "library", "maintenance",
        "millennium", "misspell", "noticeable", "occasionally", "opportunity", "persistent", "possession",
        "preferred", "privilege", "probably", "professional", "publicly", "really", "referred", "religious",
        "remember", "rhythm", "separately", "seize", "similar", "surprise", "tattoo", "tendency", "truly",
        "unfortunately", "until", "useful", "usually", "vacuum", "whether", "wherever", "length", "coming",
        "writing", "forty", "occasion", "successful", "successfully", "a lot", "as well",
    ]
    + [f"word{i}" for i in range(0)]  # placeholder kept empty
    + [
        "just", "until", "necessary", "government", "organisation", "programme", "behaviour", "colour",
        "centre", "defence", "licence", "favour", "honour", "neighbour", "theatre", "analyse", "recognise",
        "apologise", "cancelled", "travelling", "jewellery", "aluminium", "manoeuvre", "catalogue",
        "dialogue", "practise", "cheque", "aeroplane", "metre", "litre", "grey", "tyre", "kerb", "rumour",
        "labour", "flavour", "harbour", "specialise", "summarise", "modelling", "traveller", "enrolment",
        "sceptical", "skilful", "endeavour", "favourable", "realised", "organised", "recognised",
        "specialised", "programmes", "catalogues", "colours", "centres", "favourites", "organisations",
        "person", "people", "company", "companies", "service", "services", "product", "products", "market",
        "customer", "customers", "project", "projects", "system", "systems", "process", "processes",
        "result", "results", "number", "numbers", "example", "examples", "question", "questions", "answer",
        "answers", "problem", "problems", "solution", "solutions", "information", "development", "management",
        "education", "research", "technology", "community", "society", "history", "culture", "science",
        "nature", "environment", "climate", "season", "spring", "summer", "autumn", "winter", "north",
        "south", "east", "west", "left", "right", "above", "below", "inside", "outside", "near", "far",
        "here", "there", "always", "never", "sometimes", "often", "rarely", "today", "yesterday", "tomorrow",
        "morning", "evening", "night", "week", "month", "year", "hour", "minute", "second", "family",
        "friend", "school", "university", "office", "hospital", "restaurant", "hotel", "airport", "station",
        "train", "plane", "bus", "car", "bike", "money", "price", "bank", "account", "payment", "health",
        "doctor", "medicine", "sport", "music", "film", "book", "paper", "letter", "message", "phone",
        "computer", "internet", "email", "website", "password", "security", "privacy", "license",
    ],
)

# Continue in same file for remaining locales — keep pairs high-confidence.
COMMON_ES_WL = [
    "que", "porque", "también", "haber", "hacer", "hacerlo", "yendo", "quiero", "necesito", "gracias",
    "bien", "después", "ahí", "aquí", "allá", "más", "está", "están", "excelente", "problema", "interesante",
    "posiblemente", "probablemente", "definitivamente", "inmediatamente", "oportunidad", "experiencia",
    "recomendación", "comunicación", "información", "atención", "situación", "relación", "organización",
    "educación", "dirección", "acción", "opción", "sección", "conexión", "decisión", "discusión", "conclusión",
    "versión", "revisión", "televisión", "avión", "camión", "reunión", "canción", "corazón", "razón",
    "por favor", "buenos días", "buenas tardes", "buenas noches", "adiós", "perdón", "hola", "dónde",
    "quién", "cuándo", "cuánto", "ningún", "algún", "alguien", "nadie", "siempre", "nunca", "todavía",
    "ahora", "antes", "mientras", "aunque", "entonces", "además", "tampoco", "persona", "personas", "casa",
    "trabajo", "día", "noche", "semana", "mes", "año", "tiempo", "lugar", "país", "ciudad", "mundo", "vida",
    "agua", "amigo", "familia", "comida", "escuela", "universidad", "empresa", "dinero", "viaje", "salud",
    "gobierno", "problema", "solución", "pregunta", "respuesta", "idea", "sociedad", "cultura", "historia",
    "arte", "ciencia", "tecnología", "naturaleza", "clima", "verano", "invierno", "aquí", "allí", "mucho",
    "poco", "bastante", "otro", "otra", "este", "esta", "ese", "esa", "mismo", "misma", "cada", "cualquier",
    "norte", "sur", "este", "oeste", "izquierda", "derecha", "cerca", "lejos", "dentro", "fuera", "el", "la",
    "los", "las", "un", "una", "de", "en", "y", "a", "por", "con", "para", "como", "pero", "si", "no", "sí",
    "es", "son", "ser", "estar", "tiene", "pueden", "hacer", "dijo", "va", "fue", "hay", "muy", "todo",
    "hombre", "mujer", "niño", "niña", "amigo", "amiga", "mañana", "tarde", "hora", "minuto", "segundo",
    "vez", "veces", "pueblo", "calle", "camino", "muerte", "amor", "paz", "guerra", "fuego", "tierra", "aire",
    "sol", "luna", "estrella", "cielo", "mar", "río", "montaña", "árbol", "flor", "animal", "perro", "gato",
    "pájaro", "bebida", "pan", "leche", "café", "té", "vino", "cerveza", "carne", "pescado", "fruta",
    "verdura", "libro", "papel", "carta", "mensaje", "teléfono", "ordenador", "internet", "correo",
    "profesor", "estudiante", "clase", "examen", "nota", "oficina", "jefe", "empleado", "cliente", "producto",
    "servicio", "precio", "euro", "banco", "cuenta", "pago", "compra", "venta", "tienda", "mercado", "coche",
    "autobús", "tren", "barco", "bicicleta", "vacaciones", "hotel", "restaurante", "cine", "teatro", "música",
    "película", "deporte", "fútbol", "partido", "juego", "médico", "hospital", "enfermedad", "medicina",
    "política", "ley", "derecho", "justicia", "libertad", "verdad", "mentira", "error", "éxito", "opinión",
    "duda", "certeza", "esperanza", "miedo", "alegría", "tristeza", "sorpresa", "interés", "cuidado",
    "respeto", "confianza", "amistad", "primavera", "otoño", "ambiente",
]

write_pack(
    "es-ES",
    "Spanish (Spain)",
    pairs(
        "qe\tque", "qeu\tque", "qiu\tque", "qie\tque",
        "porqe\tporque", "xq\tporque", "porke\tporque", "porq\tporque",
        "tb\ttambién", "tmb\ttambién", "tambien\ttambién", "tambein\ttambién", "tanbien\ttambién",
        "haver\thaber", "aber\thaber", "aser\thacer", "acer\thacer", "hazer\thacer",
        "aserlo\thacerlo", "acerlo\thacerlo", "llendo\tyendo", "yendoo\tyendo",
        "kiero\tquiero", "qiero\tquiero", "quieroo\tquiero",
        "nesesito\tnecesito", "nesecito\tnecesito", "necesitto\tnecesito",
        "grax\tgracias", "grasias\tgracias", "graciaz\tgracias",
        "bn\tbien", "vien\tbien", "biem\tbien",
        "dps\tdespués", "despues\tdespués", "despoues\tdespués",
        "ahií\tahí", "aquii\taquí", "akí\taquí", "aya\tallá",
        "maas\tmás", "estaa\testá", "estta\testá", "esttan\testán", "estánn\testán",
        "tambienn\ttambién", "exelente\texcelente", "ecelente\texcelente",
        "problama\tproblema", "probelma\tproblema",
        "interezante\tinteresante", "interesnte\tinteresante",
        "posiblmente\tposiblemente", "posiblemnte\tposiblemente",
        "provalmente\tprobablemente", "probablmente\tprobablemente",
        "definitivamnete\tdefinitivamente", "imediatamente\tinmediatamente",
        "oportuidad\toportunidad", "esperiencia\texperiencia", "experencia\texperiencia",
        "recomendacion\trecomendación", "recomendasion\trecomendación",
        "comunicacion\tcomunicación", "comunicasión\tcomunicación",
        "informacion\tinformación", "informasión\tinformación",
        "atencion\tatención", "atension\tatención",
        "situacion\tsituación", "situasión\tsituación",
        "relacion\trelación", "relasión\trelación",
        "organizacion\torganización", "organisación\torganización",
        "educacion\teducación", "educasión\teducación",
        "direccion\tdirección", "direcsión\tdirección",
        "accion\tacción", "axión\tacción", "opcion\topción", "opsión\topción",
        "seccion\tsección", "secsión\tsección", "conexion\tconexión", "conesión\tconexión",
        "decision\tdecisión", "discusion\tdiscusión", "conclusion\tconclusión",
        "version\tversión", "revision\trevisión", "television\ttelevisión",
        "avion\tavión", "abión\tavión", "camion\tcamión", "reunion\treunión",
        "cansion\tcanción", "corazon\tcorazón", "corasón\tcorazón", "razon\trazón", "rasón\trazón",
        "talvez\ttal vez", "talves\ttal vez", "sinembargo\tsin embargo", "sinebargo\tsin embargo",
        "atraves\ta través", "a traves\ta través", "sobretodo\tsobre todo", "encuanto\ten cuanto",
        "dehecho\tde hecho", "porfavor\tpor favor", "porfabor\tpor favor",
        "muchasgracias\tmuchas gracias", "buenosdias\tbuenos días", "buenos dias\tbuenos días",
        "buenastardes\tbuenas tardes", "buenasnoches\tbuenas noches",
        "adioz\tadiós", "perdon\tperdón", "dnd\tdónde", "dondee\tdónde",
        "qien\tquién", "quiénn\tquién", "comó\tcómo", "cómoo\tcómo",
        "cuandó\tcuándo", "cuándoo\tcuándo", "cuantó\tcuánto", "cuantoo\tcuánto",
        "ningun\tningún", "ningunno\tningún", "algun\talgún", "algunn\talgún",
        "alguin\talguien", "alguíen\talguien", "nadien\tnadie", "nadíe\tnadie",
        "siempe\tsiempre", "siempree\tsiempre", "nuncaa\tnunca", "todabia\ttodavía",
        "aora\tahora", "ahoraa\tahora", "antess\tantes", "mientraz\tmientras",
        "aunqe\taunque", "entonses\tentonces",
    ),
    COMMON_ES_WL + ["tal vez", "sin embargo", "a través", "sobre todo", "en cuanto", "de hecho", "muchas gracias"],
)

write_pack(
    "es-AR",
    "Spanish (Argentina)",
    pairs(
        "qe\tque", "qeu\tque", "porqe\tporque", "xq\tporque",
        "tb\ttambién", "tmb\ttambién", "tambien\ttambién",
        "kiero\tquiero", "qiero\tquiero", "nesesito\tnecesito", "nesecito\tnecesito",
        "grax\tgracias", "grasias\tgracias", "bn\tbien", "dps\tdespués", "despues\tdespués",
        "ahií\tahí", "aquii\taquí", "maas\tmás", "estaa\testá", "esttan\testán",
        "exelente\texcelente", "problama\tproblema", "probelma\tproblema",
        "interezante\tinteresante", "interesnte\tinteresante",
        "recomendacion\trecomendación", "comunicacion\tcomunicación",
        "informacion\tinformación", "atencion\tatención", "situacion\tsituación",
        "relacion\trelación", "organizacion\torganización", "educacion\teducación",
        "direccion\tdirección", "accion\tacción", "opcion\topción",
        "decision\tdecisión", "version\tversión", "corazon\tcorazón", "razon\trazón",
        "talvez\ttal vez", "sinembargo\tsin embargo", "atraves\ta través",
        "porfavor\tpor favor", "buenosdias\tbuenos días", "adioz\tadiós", "perdon\tperdón",
        "dnd\tdónde", "dondee\tdónde", "qien\tquién", "cuandó\tcuándo", "cuantó\tcuánto",
        "ningun\tningún", "algun\talgún", "alguin\talguien", "nadien\tnadie",
        "todabia\ttodavía", "aora\tahora", "haver\thaber", "aser\thacer", "aserlo\thacerlo",
        "llendo\tyendo", "posiblmente\tposiblemente", "probablmente\tprobablemente",
        "imediatamente\tinmediatamente", "oportuidad\toportunidad",
        "esperiencia\texperiencia", "experencia\texperiencia",
        "quilomboo\tquilombo", "bárbaroo\tbárbaro", "barbaro\tbárbaro",
        "geniall\tgenial", "dalee\tdale", "listoo\tlisto", "claroo\tclaro",
        "obvioo\tobvio", "exactoo\texacto", "perfectoo\tperfecto",
        "asíí\tasí", "acáá\tacá", "laburandoo\tlaburando",
        "colectivoo\tcolectivo", "subtee\tsubte", "facturaa\tfactura",
        "bondii\tbondi", "laburoo\tlaburo",
    ),
    COMMON_ES_WL
    + ["vos", "che", "laburo", "laburando", "bondi", "colectivo", "subte", "factura", "quilombo", "bárbaro", "genial", "dale", "listo", "claro", "obvio", "exacto", "perfecto", "así", "acá"],
)

if __name__ == "__main__":
    print("Wrote en-GB, es-ES, es-AR — run _expand_pack_sources_rest.py next.")
