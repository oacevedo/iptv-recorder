# Tareas pendientes

Lista de mejoras por orden de prioridad.

## Hecho

- [x] **1. Que el PC no se duerma a mitad de la grabación**
  Mientras hay una grabación en curso se le pide a Windows que no suspenda el sistema
  (la pantalla puede apagarse igualmente). Para las grabaciones programadas se arma un
  temporizador que despierta el equipo dos minutos antes del margen de arranque. Se
  puede desactivar en Ajustes. Si el plan de energía no permite los temporizadores de
  reactivación, la app lo avisa en la barra de estado. Ver `PowerManager.cs`.

## Siguientes

- [ ] **2. Margen al final de la grabación**
  Existe margen de arranque (`LeadSeconds`) pero no de cierre. Los partidos se alargan
  con descuentos y prórrogas y hoy hay que inflar la duración a mano.

- [ ] **3. Aviso al terminar o al fallar**
  Notificación en la bandeja cuando una grabación acaba bien o falla, para no
  descubrirlo al día siguiente.

- [ ] **4. Comprobar el espacio en disco**
  Dos horas en alta definición ocupan unos 5 GB. Avisar antes de empezar si no cabe.

- [ ] **5. Logotipos de los canales**
  Ya se leen de la lista (`tvg-logo`) y se guardan en `Channel.Logo`, pero no se
  muestran. Con más de 3000 canales, buscar por icono es más rápido.

- [ ] **6. Favoritos**
  Marcar los canales que se usan de verdad y poder filtrar solo por ellos.

- [ ] **7. Registro en archivo**
  Hoy un fallo solo deja una línea en la columna Detalle. Un log en disco permite
  saber qué pasó en una grabación de madrugada.

## Proyecto grande

- [ ] **8. Guía de programación (EPG)**
  Todos los canales de la lista traen `tvg-id`, así que el proveedor tiene datos de
  programación. Permitiría elegir el partido de una guía en vez de escribir la hora a
  mano, rellenando canal, inicio y duración solos.

## Descartado por ahora

- **Firmar el ejecutable.** Windows muestra una advertencia al abrir el programa porque
  no está firmado. Un certificado cuesta dinero; de momento basta con avisar a quien
  reciba el enlace.
