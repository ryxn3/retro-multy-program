using System.Text.RegularExpressions;

namespace RetroRadio;

enum Language { English, German, Spanish, French, Polish, Swedish, Russian, Japanese }

/// <summary>
/// The radio's own words in eight languages. Only the radio's text is translated (settings, messages,
/// hints); song titles are never looked up. Keys are the English text; anything missing stays English.
/// </summary>
static partial class Lang
{
    public static Language Current = Language.English;

    /// <summary>Each language in its own words, as the LANGUAGE setting shows it.</summary>
    public static readonly string[] Names = ["ENGLISH", "DEUTSCH", "ESPAÑOL", "FRANÇAIS", "POLSKI", "SVENSKA", "РУССКИЙ", "日本語"];

    static readonly string[] Hellos = ["HELLO", "HALLO", "HOLA", "BONJOUR", "CZEŚĆ", "HEJ", "ПРИВЕТ", "こんにちは"];
    static readonly string[] Goodbyes = ["GOOD BYE", "TSCHÜSS", "ADIÓS", "AU REVOIR", "DO WIDZENIA", "HEJ DÅ", "ПОКА", "さようなら"];

    public static string Hello => Hellos[(int)Current];

    /// <summary>The built-in radios' names in Japanese, for JDM style.</summary>
    public static readonly Dictionary<string, string> JapaneseModelNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SILVER 2000"] = "シルバー2000",
        ["MIDNIGHT 99"] = "ミッドナイト99",
        ["NEON 2003"] = "ネオン2003",
        ["WOODGRAIN 84"] = "木目84",
        ["CARBON RACE"] = "カーボンレース",
        ["ROYAL 76"] = "ロイヤル76",
        ["FIELD UNIT"] = "フィールド機",
        ["AURA WHITE"] = "オーラ白",
        ["IMPERIAL GOLD"] = "インペリアル金",
        ["XPLOSION"] = "エクスプロージョン",
        ["BEL-AIRE 57"] = "ベルエア57",
        ["SHADOW S1"] = "影 S1",
        ["GROOVE 72"] = "グルーヴ72",
        ["HAULER HX"] = "トラック野郎 HX",
        ["BUBBLE Y2K"] = "バブルY2K",
        ["PRISM X9"] = "プリズムX9",
        ["NOVA TAB"] = "ノヴァ タブ",
        ["AURORA STRIP"] = "オーロラ ストリップ",
        ["VECTOR PORTRAIT"] = "ベクター 縦型",
        ["KENSEI DDX-9"] = "剣聖 DDX-9",
        ["NAKAMURA TD-1200"] = "中村 TD-1200",
        ["XPLODE GT-2001"] = "エクスプロード GT",
        ["CARROZZA DEH-9"] = "カロッツァ DEH-9",
        ["KAIDO RACER"] = "街道レーサー",
        ["TOUGE 86"] = "峠 86",
        ["SAKURA POP"] = "さくらポップ",
        ["DEKOTORA GOLD"] = "デコトラ 金",
        ["VAPOR 1989"] = "ヴェイパー 1989",
        ["MIAMI NIGHTS"] = "マイアミ ナイト",
        ["POLAR WHITE"] = "ポーラー 白",
        ["MILSPEC MS-4"] = "ミルスペック MS-4",
        ["CRUISEMASTER 70"] = "クルーズマスター 70",
        ["TITAN AMP 2400"] = "タイタン 2400",
        ["POCKETSONIC PS-89"] = "ポケットソニック",
        ["SUBURBIA 99"] = "サバービア 99",
        ["LOWRIDER GOLD"] = "ローライダー 金",
        ["TAPEWORKS 84"] = "テープワークス 84",
        ["ORBIT O8"] = "オービット O8",
        ["NEBULA X"] = "ネビュラ X",
    };
    public static string Goodbye => Goodbyes[(int)Current];

    // English → German, Spanish, French, Polish, Swedish, Russian, Japanese.
    static readonly Dictionary<string, string[]> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        // Settings.
        ["RADIO MODEL"] = ["RADIOMODELL", "MODELO DE RADIO", "MODÈLE DE RADIO", "MODEL RADIA", "RADIOMODELL", "МОДЕЛЬ РАДИО", "ラジオモデル"],
        ["IMPORT RADIO"] = ["RADIO IMPORTIEREN", "IMPORTAR RADIO", "IMPORTER RADIO", "IMPORTUJ RADIO", "IMPORTERA RADIO", "ИМПОРТ РАДИО", "ラジオ読込"],
        ["COLOR"] = ["FARBE", "COLOR", "COULEUR", "KOLOR", "FÄRG", "ЦВЕТ", "色"],
        ["VISUALIZER"] = ["VISUALISIERUNG", "VISUALIZADOR", "VISUALISEUR", "WIZUALIZACJA", "VISUALISERING", "ВИЗУАЛИЗАЦИЯ", "ビジュアライザー"],
        ["MUSIC SERVICE"] = ["MUSIKDIENST", "SERVICIO MUSICAL", "SERVICE MUSICAL", "SERWIS MUZYCZNY", "MUSIKTJÄNST", "МУЗ. СЕРВИС", "音楽サービス"],
        ["ONLINE MUSIC"] = ["ONLINE-MUSIK", "MÚSICA ONLINE", "MUSIQUE EN LIGNE", "MUZYKA ONLINE", "ONLINEMUSIK", "ОНЛАЙН МУЗЫКА", "オンライン音楽"],
        ["DISPLAY VIDEO"] = ["DISPLAY-VIDEO", "VÍDEO EN PANTALLA", "VIDÉO D'ÉCRAN", "WIDEO NA EKRANIE", "SKÄRMVIDEO", "ВИДЕО НА ДИСПЛЕЕ", "画面ビデオ"],
        ["LIGHTING"] = ["BELEUCHTUNG", "ILUMINACIÓN", "ÉCLAIRAGE", "OŚWIETLENIE", "BELYSNING", "ПОДСВЕТКА", "照明"],
        ["BRIGHTNESS"] = ["HELLIGKEIT", "BRILLO", "LUMINOSITÉ", "JASNOŚĆ", "LJUSSTYRKA", "ЯРКОСТЬ", "明るさ"],
        ["FALL SPEED"] = ["FALLTEMPO", "CAÍDA", "CHUTE", "SPADANIE", "FALLHASTIGHET", "СПАД", "減衰速度"],
        ["PEAK HOLD"] = ["SPITZE HALTEN", "PICOS", "CRÊTES", "SZCZYTY", "TOPPHÅLL", "ПИКИ", "ピーク保持"],
        ["TIME DISPLAY"] = ["ZEITANZEIGE", "TIEMPO", "AFFICHAGE TEMPS", "CZAS", "TIDSVISNING", "ВРЕМЯ", "時間表示"],
        ["LYRICS"] = ["SONGTEXT", "LETRAS", "PAROLES", "TEKSTY", "LÅTTEXT", "ТЕКСТ ПЕСНИ", "歌詞"],
        ["PLAY SPEED"] = ["TEMPO", "VELOCIDAD", "VITESSE", "PRĘDKOŚĆ", "HASTIGHET", "СКОРОСТЬ", "再生速度"],
        ["SEEK STEP"] = ["SPRUNGWEITE", "SALTO", "SAUT", "SKOK", "HOPPSTEG", "ПЕРЕМОТКА", "スキップ幅"],
        ["REPEAT"] = ["WIEDERHOLEN", "REPETIR", "RÉPÉTER", "POWTARZAJ", "UPPREPA", "ПОВТОР", "リピート"],
        ["SHUFFLE"] = ["ZUFALL", "ALEATORIO", "ALÉATOIRE", "LOSOWO", "BLANDA", "СЛУЧАЙНО", "シャッフル"],
        ["STARTUP ANIM"] = ["START-ANIMATION", "ANIM. INICIO", "ANIM. DÉMARRAGE", "ANIM. STARTU", "STARTANIMATION", "АНИМ. ВКЛ.", "起動アニメ"],
        ["SHUTDOWN ANIM"] = ["AUS-ANIMATION", "ANIM. APAGADO", "ANIM. ARRÊT", "ANIM. WYŁĄCZ.", "STÄNGANIMATION", "АНИМ. ВЫКЛ.", "終了アニメ"],
        ["STARTUP SOUND"] = ["STARTTON", "SONIDO INICIO", "SON DÉMARRAGE", "DŹWIĘK STARTU", "STARTLJUD", "ЗВУК ВКЛ.", "起動音"],
        ["SOUND VOLUME"] = ["TON-LAUTSTÄRKE", "VOLUMEN SONIDO", "VOLUME DU SON", "GŁOŚNOŚĆ DŹWIĘKU", "LJUDVOLYM", "ГРОМКОСТЬ ЗВУКА", "効果音量"],
        ["SHUTDOWN SOUND"] = ["AUS-TON", "SONIDO APAGADO", "SON D'ARRÊT", "DŹWIĘK WYŁĄCZ.", "STÄNGLJUD", "ЗВУК ВЫКЛ.", "終了音"],
        ["CUSTOM SOUND"] = ["EIGENER TON", "SONIDO PROPIO", "SON PERSO", "WŁASNY DŹWIĘK", "EGET LJUD", "СВОЙ ЗВУК", "カスタム音"],
        ["WINDOW SIZE"] = ["FENSTERGRÖSSE", "TAMAÑO", "TAILLE", "ROZMIAR", "FÖNSTERSTORLEK", "РАЗМЕР", "画面サイズ"],
        ["ALWAYS ON TOP"] = ["IMMER OBEN", "SIEMPRE ENCIMA", "TOUJOURS DEVANT", "ZAWSZE NA WIERZCHU", "ALLTID ÖVERST", "ПОВЕРХ ОКОН", "常に手前"],
        ["IDLE DEMO"] = ["DEMO IM LEERLAUF", "DEMO EN REPOSO", "DÉMO AU REPOS", "DEMO", "VILODEMO", "ДЕМО", "デモ"],
        ["CLEAR PLAYLIST"] = ["LISTE LEEREN", "VACIAR LISTA", "VIDER LA LISTE", "WYCZYŚĆ LISTĘ", "TÖM LISTAN", "ОЧИСТИТЬ СПИСОК", "リスト消去"],
        ["EXIT"] = ["ZURÜCK", "SALIR", "QUITTER", "WYJŚCIE", "TILLBAKA", "ВЫХОД", "戻る"],
        ["LANGUAGE"] = ["SPRACHE", "IDIOMA", "LANGUE", "JĘZYK", "SPRÅK", "ЯЗЫК", "言語"],
        ["JDM STYLE"] = ["JDM-STIL", "ESTILO JDM", "STYLE JDM", "STYL JDM", "JDM-STIL", "СТИЛЬ JDM", "JDMスタイル"],
        ["SETTINGS"] = ["EINSTELLUNGEN", "AJUSTES", "RÉGLAGES", "USTAWIENIA", "INSTÄLLNINGAR", "НАСТРОЙКИ", "設定"],

        // Setting values.
        ["ON"] = ["AN", "SÍ", "OUI", "WŁ", "PÅ", "ВКЛ", "オン"],
        ["OFF"] = ["AUS", "NO", "NON", "WYŁ", "AV", "ВЫКЛ", "オフ"],
        ["LOW"] = ["NIEDRIG", "BAJO", "BAS", "NISKA", "LÅG", "НИЗКАЯ", "低"],
        ["MID"] = ["MITTEL", "MEDIO", "MOYEN", "ŚREDNIA", "MELLAN", "СРЕДНЯЯ", "中"],
        ["HIGH"] = ["HOCH", "ALTO", "HAUT", "WYSOKA", "HÖG", "ВЫСОКАЯ", "高"],
        ["SLOW"] = ["LANGSAM", "LENTO", "LENT", "WOLNO", "LÅNGSAM", "МЕДЛЕННО", "遅い"],
        ["NORMAL"] = ["NORMAL", "NORMAL", "NORMAL", "NORMALNIE", "NORMAL", "НОРМА", "普通"],
        ["FAST"] = ["SCHNELL", "RÁPIDO", "RAPIDE", "SZYBKO", "SNABB", "БЫСТРО", "速い"],
        ["ELAPSED"] = ["VERSTRICHEN", "TRANSCURRIDO", "ÉCOULÉ", "UPŁYNĘŁO", "FÖRFLUTEN", "ПРОШЛО", "経過"],
        ["REMAINING"] = ["VERBLEIBEND", "RESTANTE", "RESTANT", "POZOSTAŁO", "ÅTERSTÅR", "ОСТАЛОСЬ", "残り"],
        ["ALL"] = ["ALLE", "TODO", "TOUT", "WSZYSTKO", "ALLA", "ВСЕ", "全曲"],
        ["ONE"] = ["EINEN", "UNA", "UNE", "JEDEN", "EN", "ОДИН", "1曲"],
        ["NIGHT"] = ["NACHT", "NOCHE", "NUIT", "NOC", "NATT", "НОЧЬ", "夜"],
        ["DAY"] = ["TAG", "DÍA", "JOUR", "DZIEŃ", "DAG", "ДЕНЬ", "昼"],
        ["PICK FILE"] = ["DATEI WÄHLEN", "ELEGIR ARCHIVO", "CHOISIR", "WYBIERZ PLIK", "VÄLJ FIL", "ВЫБРАТЬ ФАЙЛ", "ファイル選択"],
        ["PRESS AGAIN"] = ["NOCHMAL DRÜCKEN", "PULSA OTRA VEZ", "ENCORE UNE FOIS", "NACIŚNIJ PONOWNIE", "TRYCK IGEN", "ЕЩЁ РАЗ", "もう一度"],
        ["EMPTY"] = ["LEER", "VACÍA", "VIDE", "PUSTA", "TOM", "ПУСТО", "空"],
        ["= MUSIC"] = ["= MUSIK", "= MÚSICA", "= MUSIQUE", "= MUZYKA", "= MUSIK", "= МУЗЫКА", "＝音楽"],
        ["RANDOM"] = ["ZUFÄLLIG", "ALEATORIO", "ALÉATOIRE", "LOSOWO", "SLUMP", "СЛУЧАЙНО", "ランダム"],
        ["CUSTOM"] = ["EIGENER", "PROPIO", "PERSO", "WŁASNY", "EGET", "СВОЙ", "カスタム"],
        ["SIGN IN"] = ["ANMELDEN", "INICIAR SESIÓN", "CONNEXION", "ZALOGUJ", "LOGGA IN", "ВОЙТИ", "ログイン"],
        ["SIGNED IN"] = ["ANGEMELDET", "CONECTADO", "CONNECTÉ", "ZALOGOWANO", "INLOGGAD", "ВХОД ВЫПОЛНЕН", "ログイン済み"],
        ["SET UP"] = ["EINRICHTEN", "CONFIGURAR", "CONFIGURER", "KONFIGURUJ", "STÄLL IN", "НАСТРОИТЬ", "設定する"],
        ["PICK .RDV FILE"] = [".RDV WÄHLEN", "ELEGIR .RDV", "CHOISIR .RDV", "WYBIERZ .RDV", "VÄLJ .RDV", "ВЫБРАТЬ .RDV", ".RDV選択"],
        ["MODELS"] = ["MODELLE", "MODELOS", "MODÈLES", "MODELI", "MODELLER", "МОДЕЛЕЙ", "機種"],
        ["SEC"] = ["S", "S", "S", "S", "S", "С", "秒"],
        ["TRK"] = ["TITEL", "PISTAS", "PISTES", "UTW.", "SPÅR", "ТРЕК", "曲"],
        ["3D FAN"] = ["3D-FÄCHER", "ABANICO 3D", "ÉVENTAIL 3D", "WACHLARZ 3D", "3D-SOLFJÄDER", "3D ВЕЕР", "3Dファン"],
        ["3D GRID"] = ["3D-GITTER", "REJILLA 3D", "GRILLE 3D", "SIATKA 3D", "3D-RUTNÄT", "3D СЕТКА", "3Dグリッド"],
        ["BARS"] = ["BALKEN", "BARRAS", "BARRES", "SŁUPKI", "STAPLAR", "ПОЛОСЫ", "バー"],
        ["SCOPE"] = ["OSZILLOSKOP", "OSCILOSCOPIO", "OSCILLO", "OSCYLOSKOP", "OSCILLOSKOP", "ОСЦИЛЛОГРАФ", "波形"],
        ["VIDEO"] = ["VIDEO", "VÍDEO", "VIDÉO", "WIDEO", "VIDEO", "ВИДЕО", "ビデオ"],
        ["ICE"] = ["EIS", "HIELO", "GLACE", "LÓD", "IS", "ЛЁД", "アイス"],
        ["AQUA"] = ["AQUA", "AGUA", "AQUA", "AKWA", "AKVA", "АКВА", "アクア"],
        ["AMBER"] = ["BERNSTEIN", "ÁMBAR", "AMBRE", "BURSZTYN", "BÄRNSTEN", "ЯНТАРЬ", "琥珀"],
        ["GREEN"] = ["GRÜN", "VERDE", "VERT", "ZIELONY", "GRÖN", "ЗЕЛЁНЫЙ", "緑"],
        ["BLUE"] = ["BLAU", "AZUL", "BLEU", "NIEBIESKI", "BLÅ", "СИНИЙ", "青"],
        ["RED"] = ["ROT", "ROJO", "ROUGE", "CZERWONY", "RÖD", "КРАСНЫЙ", "赤"],

        // Display and messages.
        ["NO DISC"] = ["KEINE DISC", "SIN DISCO", "PAS DE DISQUE", "BRAK PŁYTY", "INGEN SKIVA", "НЕТ ДИСКА", "ディスクなし"],
        ["DROP MUSIC FILES OR FOLDERS HERE  -  OR PRESS OPEN / FOLDER"] = ["MUSIK HIERHER ZIEHEN  -  ODER OPEN / FOLDER DRÜCKEN", "ARRASTRA MÚSICA AQUÍ  -  O PULSA OPEN / FOLDER", "DÉPOSEZ LA MUSIQUE ICI  -  OU APPUYEZ SUR OPEN / FOLDER", "UPUŚĆ TU MUZYKĘ  -  LUB NACIŚNIJ OPEN / FOLDER", "SLÄPP MUSIK HÄR  -  ELLER TRYCK OPEN / FOLDER", "ПЕРЕТАЩИТЕ МУЗЫКУ СЮДА  -  ИЛИ НАЖМИТЕ OPEN / FOLDER", "音楽ファイルをここにドロップ　または OPEN / FOLDER"],
        ["SETTINGS - CLICK OR USE ARROW KEYS TO CHANGE"] = ["EINSTELLUNGEN - KLICKEN ODER PFEILTASTEN", "AJUSTES - HAZ CLIC O USA LAS FLECHAS", "RÉGLAGES - CLIQUEZ OU UTILISEZ LES FLÈCHES", "USTAWIENIA - KLIKNIJ LUB UŻYJ STRZAŁEK", "INSTÄLLNINGAR - KLICKA ELLER ANVÄND PILARNA", "НАСТРОЙКИ - ЩЁЛКНИТЕ ИЛИ ИСПОЛЬЗУЙТЕ СТРЕЛКИ", "設定 - クリックまたは矢印キーで変更"],
        ["LIST {0} TRK - DOUBLE-CLICK TO PLAY"] = ["LISTE {0} TITEL - DOPPELKLICK ZUM ABSPIELEN", "LISTA {0} PISTAS - DOBLE CLIC PARA REPRODUCIR", "LISTE {0} PISTES - DOUBLE-CLIC POUR LIRE", "LISTA {0} UTW. - KLIKNIJ DWA RAZY, ABY GRAĆ", "LISTA {0} SPÅR - DUBBELKLICKA FÖR ATT SPELA", "СПИСОК {0} ТРЕК. - ДВОЙНОЙ ЩЕЛЧОК ДЛЯ ВОСПРОИЗВЕДЕНИЯ", "リスト {0}曲 - ダブルクリックで再生"],
        ["SEE YOU NEXT TIME"] = ["BIS ZUM NÄCHSTEN MAL", "HASTA LA PRÓXIMA", "À LA PROCHAINE", "DO ZOBACZENIA", "VI SES NÄSTA GÅNG", "ДО ВСТРЕЧИ", "またお会いしましょう"],
        ["VOLUME"] = ["LAUTSTÄRKE", "VOLUMEN", "VOLUME", "GŁOŚNOŚĆ", "VOLYM", "ГРОМКОСТЬ", "音量"],
        ["SPEED"] = ["TEMPO", "VELOCIDAD", "VITESSE", "PRĘDKOŚĆ", "HASTIGHET", "СКОРОСТЬ", "速度"],
        ["MUTE"] = ["STUMM", "SILENCIO", "MUET", "WYCISZ", "TYST", "БЕЗ ЗВУКА", "ミュート"],
        ["MUTED"] = ["STUMM", "SILENCIADO", "MUET", "WYCISZONO", "TYST", "БЕЗ ЗВУКА", "ミュート"],
        ["MUTE ON"] = ["STUMM AN", "SILENCIO SÍ", "MUET OUI", "WYCISZ WŁ", "LJUD AV", "ЗВУК ВЫКЛ", "ミュート オン"],
        ["MUTE OFF"] = ["STUMM AUS", "SILENCIO NO", "MUET NON", "WYCISZ WYŁ", "LJUD PÅ", "ЗВУК ВКЛ", "ミュート オフ"],
        ["REPEAT ALL"] = ["ALLE WIEDERHOLEN", "REPETIR TODO", "TOUT RÉPÉTER", "POWTARZAJ WSZYSTKO", "UPPREPA ALLA", "ПОВТОР ВСЕХ", "全曲リピート"],
        ["REPEAT ONE"] = ["EINEN WIEDERHOLEN", "REPETIR UNA", "RÉPÉTER UNE", "POWTARZAJ JEDEN", "UPPREPA EN", "ПОВТОР ОДНОГО", "1曲リピート"],
        ["REPEAT OFF"] = ["WIEDERHOLEN AUS", "REPETIR NO", "RÉPÉTITION NON", "POWTARZANIE WYŁ", "UPPREPA AV", "ПОВТОР ВЫКЛ", "リピート オフ"],
        ["SHUFFLE ON"] = ["ZUFALL AN", "ALEATORIO SÍ", "ALÉATOIRE OUI", "LOSOWO WŁ", "BLANDA PÅ", "СЛУЧАЙНО ВКЛ", "シャッフル オン"],
        ["SHUFFLE OFF"] = ["ZUFALL AUS", "ALEATORIO NO", "ALÉATOIRE NON", "LOSOWO WYŁ", "BLANDA AV", "СЛУЧАЙНО ВЫКЛ", "シャッフル オフ"],
        ["CHECK YOUR BROWSER"] = ["SIEHE BROWSER", "MIRA EL NAVEGADOR", "VOIR LE NAVIGATEUR", "SPRAWDŹ PRZEGLĄDARKĘ", "KOLLA WEBBLÄSAREN", "ПРОВЕРЬТЕ БРАУЗЕР", "ブラウザを確認"],
        ["CONNECTING..."] = ["VERBINDE...", "CONECTANDO...", "CONNEXION...", "ŁĄCZENIE...", "ANSLUTER...", "ПОДКЛЮЧЕНИЕ...", "接続中..."],
        ["KEYS SAVED"] = ["SCHLÜSSEL GESPEICHERT", "CLAVES GUARDADAS", "CLÉS ENREGISTRÉES", "KLUCZE ZAPISANE", "NYCKLAR SPARADE", "КЛЮЧИ СОХРАНЕНЫ", "キー保存済み"],
        ["NO AUDIO FILES"] = ["KEINE AUDIODATEIEN", "SIN ARCHIVOS DE AUDIO", "AUCUN FICHIER AUDIO", "BRAK PLIKÓW AUDIO", "INGA LJUDFILER", "НЕТ АУДИОФАЙЛОВ", "音楽ファイルなし"],
        ["NO SPEED ON SPOTIFY"] = ["KEIN TEMPO BEI SPOTIFY", "SIN VELOCIDAD EN SPOTIFY", "PAS DE VITESSE SUR SPOTIFY", "BRAK PRĘDKOŚCI W SPOTIFY", "INGEN HASTIGHET PÅ SPOTIFY", "СКОРОСТЬ НЕДОСТУПНА В SPOTIFY", "SPOTIFYは速度変更不可"],
        ["NOT PLAYABLE"] = ["NICHT ABSPIELBAR", "NO REPRODUCIBLE", "ILLISIBLE", "NIE MOŻNA ODTWORZYĆ", "KAN INTE SPELAS", "НЕ ВОСПРОИЗВОДИТСЯ", "再生不可"],
        ["NOTHING FOUND"] = ["NICHTS GEFUNDEN", "NADA ENCONTRADO", "RIEN TROUVÉ", "NIC NIE ZNALEZIONO", "INGET HITTAT", "НИЧЕГО НЕ НАЙДЕНО", "見つかりません"],
        ["PLAYING ON SPOTIFY"] = ["LÄUFT AUF SPOTIFY", "SONANDO EN SPOTIFY", "LECTURE SUR SPOTIFY", "GRA NA SPOTIFY", "SPELAS PÅ SPOTIFY", "ИГРАЕТ В SPOTIFY", "SPOTIFYで再生中"],
        ["WHOLE PLAYLIST ON SPOTIFY"] = ["GANZE PLAYLIST AUF SPOTIFY", "LISTA COMPLETA EN SPOTIFY", "PLAYLIST ENTIÈRE SUR SPOTIFY", "CAŁA PLAYLISTA NA SPOTIFY", "HELA SPELLISTAN PÅ SPOTIFY", "ВЕСЬ ПЛЕЙЛИСТ В SPOTIFY", "プレイリストをSPOTIFYで再生"],
        ["PLAYLIST CLEARED"] = ["LISTE GELEERT", "LISTA VACIADA", "LISTE VIDÉE", "LISTA WYCZYSZCZONA", "LISTAN TÖMD", "СПИСОК ОЧИЩЕН", "リストを消去"],
        ["READ ERROR"] = ["LESEFEHLER", "ERROR DE LECTURA", "ERREUR DE LECTURE", "BŁĄD ODCZYTU", "LÄSFEL", "ОШИБКА ЧТЕНИЯ", "読込エラー"],
        ["SIGNED OUT"] = ["ABGEMELDET", "SESIÓN CERRADA", "DÉCONNECTÉ", "WYLOGOWANO", "UTLOGGAD", "ВЫХОД ВЫПОЛНЕН", "ログアウト"],
        ["SOUND ERROR"] = ["TONFEHLER", "ERROR DE SONIDO", "ERREUR DE SON", "BŁĄD DŹWIĘKU", "LJUDFEL", "ОШИБКА ЗВУКА", "音声エラー"],
        ["SYNCED LYRICS FROM LRCLIB.NET"] = ["SONGTEXTE VON LRCLIB.NET", "LETRAS DE LRCLIB.NET", "PAROLES DE LRCLIB.NET", "TEKSTY Z LRCLIB.NET", "LÅTTEXTER FRÅN LRCLIB.NET", "ТЕКСТЫ С LRCLIB.NET", "歌詞：LRCLIB.NET"],
        ["TIMED OUT"] = ["ZEITÜBERSCHREITUNG", "TIEMPO AGOTADO", "DÉLAI DÉPASSÉ", "PRZEKROCZONO CZAS", "TIDSGRÄNS", "ВРЕМЯ ИСТЕКЛО", "タイムアウト"],
        ["VIDEO ERROR"] = ["VIDEOFEHLER", "ERROR DE VÍDEO", "ERREUR VIDÉO", "BŁĄD WIDEO", "VIDEOFEL", "ОШИБКА ВИДЕО", "ビデオエラー"],
        ["VIDEO LOADED"] = ["VIDEO GELADEN", "VÍDEO CARGADO", "VIDÉO CHARGÉE", "WIDEO WCZYTANE", "VIDEO LADDAD", "ВИДЕО ЗАГРУЖЕНО", "ビデオ読込完了"],
        ["NO VIDEO"] = ["KEIN VIDEO", "SIN VÍDEO", "PAS DE VIDÉO", "BRAK WIDEO", "INGEN VIDEO", "НЕТ ВИДЕО", "ビデオなし"],
        ["MAKE ONE WITH RADIO VIDEO CONVERTER"] = ["MIT RADIO VIDEO CONVERTER ERSTELLEN", "CRÉALO CON RADIO VIDEO CONVERTER", "CRÉEZ-EN UNE AVEC RADIO VIDEO CONVERTER", "UTWÓRZ W RADIO VIDEO CONVERTER", "GÖR EN MED RADIO VIDEO CONVERTER", "СОЗДАЙТЕ В RADIO VIDEO CONVERTER", "RADIO VIDEO CONVERTERで作成"],
        ["{0} TRACK ADDED"] = ["{0} TITEL HINZUGEFÜGT", "{0} PISTA AÑADIDA", "{0} PISTE AJOUTÉE", "DODANO {0} UTWÓR", "{0} SPÅR TILLAGT", "ДОБАВЛЕН {0} ТРЕК", "{0}曲を追加"],
        ["{0} TRACKS ADDED"] = ["{0} TITEL HINZUGEFÜGT", "{0} PISTAS AÑADIDAS", "{0} PISTES AJOUTÉES", "DODANO {0} UTW.", "{0} SPÅR TILLAGDA", "ДОБАВЛЕНО ТРЕКОВ: {0}", "{0}曲を追加"],
        ["30 SEC PREVIEW ONLY"] = ["NUR 30 S VORSCHAU", "SOLO 30 S DE MUESTRA", "APERÇU DE 30 S", "TYLKO 30 S PODGLĄDU", "BARA 30 S SMAKPROV", "ТОЛЬКО 30 С ПРЕВЬЮ", "30秒試聴のみ"],
        ["PLAYING"] = ["WIEDERGABE", "REPRODUCIENDO", "LECTURE", "ODTWARZANIE", "SPELAR", "ИГРАЕТ", "再生中"],
        ["STOPPED"] = ["GESTOPPT", "DETENIDO", "ARRÊTÉ", "ZATRZYMANO", "STOPPAD", "ОСТАНОВЛЕНО", "停止"],
        ["PAUSED"] = ["PAUSIERT", "EN PAUSA", "EN PAUSE", "WSTRZYMANO", "PAUSAD", "ПАУЗА", "一時停止"],
        ["CLASSIC"] = ["KLASSISCH", "CLÁSICO", "CLASSIQUE", "KLASYCZNY", "KLASSISK", "КЛАССИКА", "クラシック"],
        ["SCANNER"] = ["SCANNER", "ESCÁNER", "SCANNER", "SKANER", "SKANNER", "СКАНЕР", "スキャナー"],
        ["RAIN"] = ["REGEN", "LLUVIA", "PLUIE", "DESZCZ", "REGN", "ДОЖДЬ", "レイン"],
        ["STATIC"] = ["RAUSCHEN", "ESTÁTICA", "NEIGE", "SZUM", "BRUS", "ПОМЕХИ", "砂嵐"],
        ["TERMINAL"] = ["TERMINAL", "TERMINAL", "TERMINAL", "TERMINAL", "TERMINAL", "ТЕРМИНАЛ", "ターミナル"],
        ["BOUNCE"] = ["HÜPFEN", "REBOTE", "REBOND", "ODBICIE", "STUDS", "ПРЫЖОК", "バウンド"],
        ["TV OFF"] = ["TV AUS", "TV APAGADA", "TÉLÉ ÉTEINTE", "WYŁĄCZ TV", "TV AV", "ТВ ВЫКЛ", "テレビオフ"],
        ["FADE"] = ["AUSBLENDEN", "DESVANECER", "FONDU", "ZANIKANIE", "TONA UT", "ЗАТУХАНИЕ", "フェード"],
        ["DISSOLVE"] = ["AUFLÖSEN", "DISOLVER", "DISSOLUTION", "ROZPAD", "UPPLÖS", "РАСТВОРЕНИЕ", "ディゾルブ"],
        ["WIPE"] = ["WISCHEN", "BARRIDO", "BALAYAGE", "WYMAZANIE", "SVEP", "ШТОРКА", "ワイプ"],
        ["FALL"] = ["FALLEN", "CAÍDA", "CHUTE", "UPADEK", "FALL", "ПАДЕНИЕ", "フォール"],
        ["RADIO"] = ["RADIO", "RADIO", "RADIO", "RADIO", "RADIO", "РАДИО", "ラジオ"],
        ["SOUND"] = ["KLANG", "SONIDO", "SON", "DŹWIĘK", "LJUD", "ЗВУК", "サウンド"],
        ["PLAYBACK"] = ["WIEDERGABE", "REPRODUCCIÓN", "LECTURE", "ODTWARZANIE", "UPPSPELNING", "ВОСПРОИЗВЕДЕНИЕ", "再生"],
        ["DISPLAY"] = ["ANZEIGE", "PANTALLA", "AFFICHAGE", "EKRAN", "SKÄRM", "ДИСПЛЕЙ", "表示"],
        ["POWER"] = ["EIN/AUS", "ENCENDIDO", "MARCHE/ARRÊT", "ZASILANIE", "PÅ/AV", "ПИТАНИЕ", "電源"],
        ["SYSTEM"] = ["SYSTEM", "SISTEMA", "SYSTÈME", "SYSTEM", "SYSTEM", "СИСТЕМА", "システム"],
        ["ONLINE"] = ["ONLINE", "EN LÍNEA", "EN LIGNE", "ONLINE", "ONLINE", "ОНЛАЙН", "オンライン"],
        ["BACK"] = ["ZURÜCK", "ATRÁS", "RETOUR", "WSTECZ", "TILLBAKA", "НАЗАД", "戻る"],
        ["EQUALIZER"] = ["EQUALIZER", "ECUALIZADOR", "ÉGALISEUR", "KOREKTOR", "EQUALIZER", "ЭКВАЛАЙЗЕР", "イコライザー"],
        ["EQ PRESET"] = ["EQ-VOREINSTELLUNG", "PRESET EQ", "PRÉRÉGLAGE EQ", "PRESET EQ", "EQ-FÖRVAL", "ПРЕСЕТ EQ", "EQプリセット"],
        ["LOUDNESS"] = ["LOUDNESS", "LOUDNESS", "LOUDNESS", "LOUDNESS", "LOUDNESS", "ТОНКОМПЕНСАЦИЯ", "ラウドネス"],
        ["REVERB"] = ["HALL", "REVERBERACIÓN", "RÉVERB", "POGŁOS", "EKO", "РЕВЕРБЕРАЦИЯ", "リバーブ"],
        ["OUTSIDE CAR"] = ["AUSSERHALB DES AUTOS", "FUERA DEL COCHE", "HORS DE LA VOITURE", "POZA AUTEM", "UTANFÖR BILEN", "СНАРУЖИ МАШИНЫ", "車の外"],
        ["CROSSFADE"] = ["ÜBERBLENDEN", "FUNDIDO", "FONDU ENCHAÎNÉ", "PRZENIKANIE", "ÖVERTONING", "КРОССФЕЙД", "クロスフェード"],
        ["BUTTON SOUND"] = ["TASTENTON", "SONIDO BOTÓN", "SON DES TOUCHES", "DŹWIĘK PRZYCISKÓW", "KNAPPLJUD", "ЗВУК КНОПОК", "ボタン音"],
        ["KNOB SOUND"] = ["DREHKNOPF-TON", "SONIDO RUEDA", "SON DU BOUTON", "DŹWIĘK POKRĘTŁA", "RATTLJUD", "ЗВУК РУЧКИ", "ノブ音"],
        ["CLICK VOLUME"] = ["KLICK-LAUTSTÄRKE", "VOLUMEN CLIC", "VOLUME DES CLICS", "GŁOŚNOŚĆ KLIKÓW", "KLICKVOLYM", "ГРОМКОСТЬ ЩЕЛЧКОВ", "クリック音量"],
        ["SMART SHUFFLE"] = ["SMART-ZUFALL", "ALEATORIO LISTO", "ALÉATOIRE MALIN", "SPRYTNE LOSOWANIE", "SMART BLANDNING", "УМНЫЙ СЛУЧАЙНЫЙ", "スマートシャッフル"],
        ["SLEEP TIMER"] = ["SCHLAF-TIMER", "TEMPORIZADOR", "MINUTERIE", "WYŁĄCZNIK CZASOWY", "INSOMNINGSTIMER", "ТАЙМЕР СНА", "スリープタイマー"],
        ["BPM"] = ["BPM", "BPM", "BPM", "BPM", "BPM", "BPM", "BPM"],
        ["SEARCH"] = ["SUCHEN", "BUSCAR", "CHERCHER", "SZUKAJ", "SÖK", "ПОИСК", "検索"],
        ["FRAME RATE"] = ["BILDRATE", "FOTOGRAMAS", "IMAGES/S", "KLATKI/S", "BILDFREKVENS", "ЧАСТОТА КАДРОВ", "フレームレート"],
        ["FILES ONLY"] = ["NUR DATEIEN", "SOLO ARCHIVOS", "FICHIERS SEULS", "TYLKO PLIKI", "BARA FILER", "ТОЛЬКО ФАЙЛЫ", "ファイルのみ"],
        ["FILES ONLY - ONLINE IS OFF"] = ["NUR DATEIEN - ONLINE AUS", "SOLO ARCHIVOS - SIN INTERNET", "FICHIERS SEULS - EN LIGNE DÉSACTIVÉ", "TYLKO PLIKI - ONLINE WYŁĄCZONE", "BARA FILER - ONLINE AV", "ТОЛЬКО ФАЙЛЫ - ОНЛАЙН ВЫКЛ", "ファイルのみ・オンライン無効"],
        ["AUTO NIGHT"] = ["AUTO-NACHT", "NOCHE AUTOMÁTICA", "NUIT AUTO", "AUTO NOC", "AUTO NATT", "АВТО НОЧЬ", "自動夜間"],
        ["TWO-COLOR"] = ["ZWEIFARBIG", "DOS COLORES", "BICOLORE", "DWUKOLOROWY", "TVÅFÄRG", "ДВА ЦВЕТА", "2色表示"],
        ["BASS PULSE"] = ["BASS-PULS", "PULSO DE BAJOS", "PULSATION BASSES", "PULS BASU", "BASPULS", "ПУЛЬС БАСА", "低音パルス"],
        ["FLIP SCREEN"] = ["KLAPPBILDSCHIRM", "PANTALLA ABATIBLE", "ÉCRAN RABATTABLE", "ODCHYLANY EKRAN", "FÄLLSKÄRM", "ОТКИДНОЙ ЭКРАН", "フリップ画面"],
        ["CLOCK WHEN IDLE"] = ["UHR IM LEERLAUF", "RELOJ EN REPOSO", "HORLOGE AU REPOS", "ZEGAR W SPOCZYNKU", "KLOCKA VID VILA", "ЧАСЫ В ПРОСТОЕ", "待機時の時計"],
        ["VOICE LINES"] = ["SPRACHANSAGEN", "FRASES DE VOZ", "PHRASES VOCALES", "KWESTIE GŁOSOWE", "RÖSTREPLIKER", "ГОЛОСОВЫЕ ФРАЗЫ", "音声"],
        ["VOICE EVERY"] = ["ANSAGE ALLE", "VOZ CADA", "VOIX TOUTES LES", "GŁOS CO", "RÖST VAR", "ГОЛОС КАЖДЫЕ", "音声の間隔"],
        ["MINI MODE"] = ["MINI-MODUS", "MODO MINI", "MODE MINI", "TRYB MINI", "MINILÄGE", "МИНИ-РЕЖИМ", "ミニモード"],
        ["TRAY ICON"] = ["TRAY-SYMBOL", "ICONO EN BANDEJA", "ICÔNE DE ZONE", "IKONA W ZASOBNIKU", "IKON I FÄLTET", "ЗНАЧОК В ТРЕЕ", "トレイアイコン"],
        ["START WITH WINDOWS"] = ["MIT WINDOWS STARTEN", "INICIAR CON WINDOWS", "LANCER AVEC WINDOWS", "START Z WINDOWS", "STARTA MED WINDOWS", "ЗАПУСК С WINDOWS", "WINDOWSと起動"],
        ["INTERNET RADIO"] = ["INTERNETRADIO", "RADIO POR INTERNET", "RADIO INTERNET", "RADIO INTERNETOWE", "WEBBRADIO", "ИНТЕРНЕТ-РАДИО", "ネットラジオ"],
        ["SUNSET"] = ["SONNENUNTERGANG", "ATARDECER", "COUCHER DU SOLEIL", "ZACHÓD SŁOŃCA", "SOLNEDGÅNG", "ЗАКАТ", "日没"],
        ["ROOM"] = ["RAUM", "SALA", "PIÈCE", "POKÓJ", "RUM", "КОМНАТА", "部屋"],
        ["HALL"] = ["SAAL", "AUDITORIO", "SALLE", "SALA", "HALL", "ЗАЛ", "ホール"],
        ["GARAGE"] = ["PARKHAUS", "PARKING", "PARKING", "PARKING", "GARAGE", "ПАРКОВКА", "駐車場"],
        ["ECHO"] = ["ECHO", "ECO", "ÉCHO", "ECHO", "EKO", "ЭХО", "エコー"],
        ["MIN"] = ["MIN", "MIN", "MIN", "MIN", "MIN", "МИН", "分"],
        ["CIRCLE"] = ["KREIS", "CÍRCULO", "CERCLE", "KOŁO", "CIRKEL", "КРУГ", "サークル"],
        ["VU METER"] = ["VU-METER", "VÚMETRO", "VU-MÈTRE", "WSKAŹNIK VU", "VU-MÄTARE", "VU-МЕТР", "VUメーター"],
        ["CD"] = ["CD", "CD", "CD", "CD", "CD", "CD", "CD"],
        ["FIRE"] = ["FEUER", "FUEGO", "FEU", "OGIEŃ", "ELD", "ОГОНЬ", "炎"],
        ["STARS"] = ["STERNE", "ESTRELLAS", "ÉTOILES", "GWIAZDY", "STJÄRNOR", "ЗВЁЗДЫ", "星空"],
        ["BASS"] = ["BASS", "GRAVES", "GRAVES", "BAS", "BAS", "БАС", "低音"],
        ["TREBLE"] = ["HÖHEN", "AGUDOS", "AIGUS", "SOPRANY", "DISKANT", "ВЧ", "高音"],
        ["LOUD"] = ["LOUD", "LOUD", "LOUD", "LOUD", "LOUD", "ТОНК.", "ラウド"],
        ["ARROWS OR CLICK - ESC TO CLOSE"] = ["PFEILE ODER KLICKEN - ESC SCHLIESST", "FLECHAS O CLIC - ESC CIERRA", "FLÈCHES OU CLIC - ÉCHAP FERME", "STRZAŁKI LUB KLIK - ESC ZAMYKA", "PILAR ELLER KLICK - ESC STÄNGER", "СТРЕЛКИ ИЛИ ЩЕЛЧОК - ESC ЗАКРЫТЬ", "矢印かクリック　ESCで閉じる"],
        ["PLAY / PAUSE"] = ["WIEDERGABE / PAUSE", "REPRODUCIR / PAUSA", "LECTURE / PAUSE", "ODTWÓRZ / PAUZA", "SPELA / PAUS", "ВОСПР. / ПАУЗА", "再生 / 一時停止"],
        ["NEXT"] = ["WEITER", "SIGUIENTE", "SUIVANT", "NASTĘPNY", "NÄSTA", "ДАЛЕЕ", "次へ"],
        ["PREVIOUS"] = ["ZURÜCK", "ANTERIOR", "PRÉCÉDENT", "POPRZEDNI", "FÖREGÅENDE", "НАЗАД", "前へ"],
        ["SHOW RADIO"] = ["RADIO ZEIGEN", "MOSTRAR RADIO", "AFFICHER LA RADIO", "POKAŻ RADIO", "VISA RADION", "ПОКАЗАТЬ РАДИО", "ラジオを表示"],
        ["POWER OFF"] = ["AUSSCHALTEN", "APAGAR", "ÉTEINDRE", "WYŁĄCZ", "STÄNG AV", "ВЫКЛЮЧИТЬ", "電源オフ"],
        ["TUNING..."] = ["SENDER SUCHEN...", "SINTONIZANDO...", "RÉGLAGE...", "STROJENIE...", "STÄMMER IN...", "НАСТРОЙКА...", "チューニング中..."],
        ["STATION NOT AVAILABLE"] = ["SENDER NICHT VERFÜGBAR", "EMISORA NO DISPONIBLE", "STATION INDISPONIBLE", "STACJA NIEDOSTĘPNA", "STATIONEN ÄR INTE TILLGÄNGLIG", "СТАНЦИЯ НЕДОСТУПНА", "放送局に接続できません"],
        ["SEARCH STATIONS"] = ["SENDER SUCHEN", "BUSCAR EMISORAS", "CHERCHER DES STATIONS", "SZUKAJ STACJI", "SÖK STATIONER", "ПОИСК СТАНЦИЙ", "放送局を検索"],
        ["TOP STATIONS"] = ["TOP-SENDER", "EMISORAS TOP", "STATIONS POPULAIRES", "NAJLEPSZE STACJE", "TOPPSTATIONER", "ТОП СТАНЦИЙ", "人気の放送局"],
        ["JAPAN"] = ["JAPAN", "JAPÓN", "JAPON", "JAPONIA", "JAPAN", "ЯПОНИЯ", "日本"],
        ["NO CONNECTION"] = ["KEINE VERBINDUNG", "SIN CONEXIÓN", "PAS DE CONNEXION", "BRAK POŁĄCZENIA", "INGEN ANSLUTNING", "НЕТ СОЕДИНЕНИЯ", "接続なし"],
        ["LOADING"] = ["LADEN", "CARGANDO", "CHARGEMENT", "ŁADOWANIE", "LADDAR", "ЗАГРУЗКА", "読込中"],
        ["LOAD MORE..."] = ["MEHR LADEN...", "CARGAR MÁS...", "PLUS...", "WCZYTAJ WIĘCEJ...", "LADDA FLER...", "ЕЩЁ...", "さらに読込..."],
        ["SEARCH TRACKS"] = ["TITEL SUCHEN", "BUSCAR CANCIONES", "CHERCHER DES TITRES", "SZUKAJ UTWORÓW", "SÖK LÅTAR", "ПОИСК ТРЕКОВ", "曲を検索"],
        ["SEARCH PLAYLISTS"] = ["PLAYLISTS SUCHEN", "BUSCAR LISTAS", "CHERCHER DES PLAYLISTS", "SZUKAJ PLAYLIST", "SÖK SPELLISTOR", "ПОИСК ПЛЕЙЛИСТОВ", "プレイリスト検索"],
        ["MY PLAYLISTS"] = ["MEINE PLAYLISTS", "MIS LISTAS", "MES PLAYLISTS", "MOJE PLAYLISTY", "MINA SPELLISTOR", "МОИ ПЛЕЙЛИСТЫ", "マイプレイリスト"],
        ["MY LIKES"] = ["MEINE LIKES", "MIS ME GUSTA", "MES J'AIME", "MOJE POLUBIONE", "MINA GILLADE", "МОИ ЛАЙКИ", "お気に入り"],
        ["SIGN OUT"] = ["ABMELDEN", "CERRAR SESIÓN", "DÉCONNEXION", "WYLOGUJ", "LOGGA UT", "ВЫЙТИ", "ログアウト"],
        ["SWITCH SERVICE"] = ["DIENST WECHSELN", "CAMBIAR SERVICIO", "CHANGER DE SERVICE", "ZMIEŃ SERWIS", "BYT TJÄNST", "СМЕНИТЬ СЕРВИС", "サービス切替"],
        ["APP KEYS"] = ["APP-SCHLÜSSEL", "CLAVES APP", "CLÉS APP", "KLUCZE APLIKACJI", "APPNYCKLAR", "КЛЮЧИ", "アプリキー"],
        ["SET UP APP KEYS"] = ["APP-SCHLÜSSEL EINRICHTEN", "CONFIGURAR CLAVES", "CONFIGURER LES CLÉS", "KONFIGURUJ KLUCZE", "STÄLL IN APPNYCKLAR", "НАСТРОИТЬ КЛЮЧИ", "キーを設定"],
        ["SET UP APP KEY"] = ["APP-SCHLÜSSEL EINRICHTEN", "CONFIGURAR CLAVE", "CONFIGURER LA CLÉ", "KONFIGURUJ KLUCZ", "STÄLL IN APPNYCKEL", "НАСТРОИТЬ КЛЮЧ", "キーを設定"],
        ["SET UP APP KEYS FIRST"] = ["ZUERST APP-SCHLÜSSEL EINRICHTEN", "PRIMERO CONFIGURA LAS CLAVES", "CONFIGUREZ D'ABORD LES CLÉS", "NAJPIERW KONFIGURUJ KLUCZE", "STÄLL IN APPNYCKLAR FÖRST", "СНАЧАЛА НАСТРОЙТЕ КЛЮЧИ", "先にキーを設定"],
        ["SET UP APP KEY FIRST"] = ["ZUERST APP-SCHLÜSSEL EINRICHTEN", "PRIMERO CONFIGURA LA CLAVE", "CONFIGUREZ D'ABORD LA CLÉ", "NAJPIERW KONFIGURUJ KLUCZ", "STÄLL IN APPNYCKELN FÖRST", "СНАЧАЛА НАСТРОЙТЕ КЛЮЧ", "先にキーを設定"],
        ["START HERE"] = ["HIER STARTEN", "EMPIEZA AQUÍ", "COMMENCER ICI", "ZACZNIJ TUTAJ", "BÖRJA HÄR", "НАЧНИТЕ ЗДЕСЬ", "ここから"],
        ["OPENS BROWSER"] = ["ÖFFNET BROWSER", "ABRE EL NAVEGADOR", "OUVRE LE NAVIGATEUR", "OTWIERA PRZEGLĄDARKĘ", "ÖPPNAR WEBBLÄSARE", "ОТКРОЕТ БРАУЗЕР", "ブラウザを開く"],
        ["PLAYLISTS"] = ["PLAYLISTS", "LISTAS", "PLAYLISTS", "PLAYLISTY", "SPELLISTOR", "ПЛЕЙЛИСТЫ", "プレイリスト"],
        ["TRACKS"] = ["TITEL", "PISTAS", "TITRES", "UTWORY", "LÅTAR", "ТРЕКИ", "曲"],
        ["ENTER = SEARCH   ESC = BACK"] = ["ENTER = SUCHEN   ESC = ZURÜCK", "ENTER = BUSCAR   ESC = VOLVER", "ENTRÉE = CHERCHER   ÉCHAP = RETOUR", "ENTER = SZUKAJ   ESC = WSTECZ", "ENTER = SÖK   ESC = TILLBAKA", "ENTER = ПОИСК   ESC = НАЗАД", "ENTER＝検索　ESC＝戻る"],
        ["PLEASE SIGN IN"] = ["BITTE ANMELDEN", "INICIA SESIÓN", "CONNECTEZ-VOUS", "ZALOGUJ SIĘ", "LOGGA IN", "ВОЙДИТЕ", "ログインしてください"],
        ["PLEASE SIGN IN AGAIN"] = ["BITTE ERNEUT ANMELDEN", "INICIA SESIÓN DE NUEVO", "RECONNECTEZ-VOUS", "ZALOGUJ SIĘ PONOWNIE", "LOGGA IN IGEN", "ВОЙДИТЕ СНОВА", "再ログインしてください"],
        ["SIGN-IN CANCELLED"] = ["ANMELDUNG ABGEBROCHEN", "INICIO CANCELADO", "CONNEXION ANNULÉE", "LOGOWANIE ANULOWANE", "INLOGGNING AVBRUTEN", "ВХОД ОТМЕНЁН", "ログイン中止"],
        ["SIGN-IN FAILED"] = ["ANMELDUNG FEHLGESCHLAGEN", "ERROR AL INICIAR SESIÓN", "ÉCHEC DE CONNEXION", "BŁĄD LOGOWANIA", "INLOGGNINGEN MISSLYCKADES", "ОШИБКА ВХОДА", "ログイン失敗"],
        ["NEEDS SPOTIFY PREMIUM"] = ["BRAUCHT SPOTIFY PREMIUM", "REQUIERE SPOTIFY PREMIUM", "NÉCESSITE SPOTIFY PREMIUM", "WYMAGA SPOTIFY PREMIUM", "KRÄVER SPOTIFY PREMIUM", "НУЖЕН SPOTIFY PREMIUM", "SPOTIFY PREMIUMが必要"],
        ["OPEN THE SPOTIFY APP"] = ["SPOTIFY-APP ÖFFNEN", "ABRE LA APP DE SPOTIFY", "OUVREZ L'APP SPOTIFY", "OTWÓRZ APLIKACJĘ SPOTIFY", "ÖPPNA SPOTIFY-APPEN", "ОТКРОЙТЕ SPOTIFY", "SPOTIFYアプリを開いて"],
        ["TRACK NOT PLAYABLE"] = ["TITEL NICHT ABSPIELBAR", "PISTA NO REPRODUCIBLE", "PISTE ILLISIBLE", "UTWÓR NIEDOSTĘPNY", "SPÅRET KAN INTE SPELAS", "ТРЕК НЕДОСТУПЕН", "再生できない曲"],
        ["NO MEDIA"] = ["KEINE MEDIEN", "SIN MEDIOS", "AUCUN MÉDIA", "BRAK MEDIÓW", "INGA MEDIER", "НЕТ МЕДИА", "メディアなし"],
        ["READY"] = ["BEREIT", "LISTO", "PRÊT", "GOTOWE", "REDO", "ГОТОВО", "準備完了"],
        ["UP NEXT"] = ["ALS NÄCHSTES", "A CONTINUACIÓN", "ENSUITE", "NASTĘPNIE", "HÄRNÄST", "ДАЛЕЕ", "次の曲"],
        ["NO MUSIC YET"] = ["NOCH KEINE MUSIK", "AÚN SIN MÚSICA", "PAS ENCORE DE MUSIQUE", "BRAK MUZYKI", "INGEN MUSIK ÄN", "МУЗЫКИ ПОКА НЕТ", "まだ音楽がありません"],
        ["DROP MUSIC FILES OR FOLDERS HERE"] = ["MUSIKDATEIEN ODER ORDNER HIERHER ZIEHEN", "ARRASTRA AQUÍ ARCHIVOS O CARPETAS", "DÉPOSEZ ICI FICHIERS OU DOSSIERS", "UPUŚĆ TU PLIKI LUB FOLDERY", "SLÄPP MUSIKFILER ELLER MAPPAR HÄR", "ПЕРЕТАЩИТЕ СЮДА ФАЙЛЫ ИЛИ ПАПКИ", "音楽ファイルやフォルダをここにドロップ"],
        ["OR PRESS OPEN / FOLDER"] = ["ODER OPEN / FOLDER DRÜCKEN", "O PULSA OPEN / FOLDER", "OU APPUYEZ SUR OPEN / FOLDER", "LUB NACIŚNIJ OPEN / FOLDER", "ELLER TRYCK OPEN / FOLDER", "ИЛИ НАЖМИТЕ OPEN / FOLDER", "または OPEN / FOLDER を押す"],
        ["TRACK {0} OF {1}"] = ["TITEL {0} VON {1}", "PISTA {0} DE {1}", "PISTE {0} SUR {1}", "UTWÓR {0} Z {1}", "SPÅR {0} AV {1}", "ТРЕК {0} ИЗ {1}", "{1}曲中 {0}曲目"],
        ["PLAYLIST  ·  {0} TRACKS"] = ["PLAYLIST  ·  {0} TITEL", "LISTA  ·  {0} PISTAS", "PLAYLIST  ·  {0} PISTES", "PLAYLISTA  ·  {0} UTW.", "SPELLISTA  ·  {0} SPÅR", "ПЛЕЙЛИСТ  ·  {0} ТРЕК.", "プレイリスト  ·  {0}曲"],
    };

    /// <summary>The text in the current language (English when there's no translation).</summary>
    public static string T(string en)
    {
        if (Current == Language.English || string.IsNullOrEmpty(en)) return en;
        string key = en.Trim();
        if (!Table.TryGetValue(key, out var t)) return en;
        string s = t[(int)Current - 1];
        // Sentence-case keys (the modern screens) get sentence-case translations.
        if (key.Any(char.IsLower) && s.Length > 1) s = char.ToUpper(s[0]) + s[1..].ToLowerInvariant();
        return en.EndsWith(' ') ? s + new string(' ', en.Length - en.TrimEnd().Length) : s;
    }

    /// <summary>A translated pattern with numbers or names filled in, e.g. F("{0} TRACKS ADDED", 3).</summary>
    public static string F(string pattern, params object[] args) => string.Format(T(pattern), args);

    /// <summary>A setting value: an exact phrase ("ON"), or a number with a unit ("12 TRK", "5 SEC").</summary>
    public static string Value(string v)
    {
        if (Current == Language.English) return v;
        if (Table.ContainsKey(v.Trim())) return T(v);
        var m = NumberUnit().Match(v);
        return m.Success && Table.ContainsKey(m.Groups[2].Value) ? $"{m.Groups[1].Value} {T(m.Groups[2].Value)}" : v;
    }

    [GeneratedRegex(@"^(\d+)\s+([A-Z]+)$")]
    private static partial Regex NumberUnit();
}
