﻿using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using Microsoft.DirectX;
using Microsoft.DirectX.DirectInput;
using TGC.Core.Camara;
using TGC.Core.Direct3D;
using TGC.Core.Input;
using TGC.Core.Utils;
using TGC.Examples.Collision.SphereCollision;
using TGC.Core.Collision;
using TGC.Core.BoundingVolumes;
using TGC.Core.Geometry;
using TGC.Group.Model;

namespace TGC.Examples.Camara
{
    /// <summary>
    ///     Camara en primera persona que utiliza matrices de rotacion, solo almacena las rotaciones en updown y costados.
    ///     Ref: http://www.riemers.net/eng/Tutorials/XNA/Csharp/Series4/Mouse_camera.php
    ///     Autor: Rodrigo Garcia.
    /// </summary>
    public class TgcFpsCamera : TgcCamera
    {
        Vector3 newPosition = new Vector3(0, 0, 0);
        private readonly Point mouseCenter; //Centro de mause 2D para ocultarlo.

        //Se mantiene la matriz rotacion para no hacer este calculo cada vez.
        private Matrix cameraRotation;

        //Direction view se calcula a partir de donde se quiere ver con la camara inicialmente. por defecto se ve en -Z.
        private Vector3 directionView;

        //No hace falta la base ya que siempre es la misma, la base se arma segun las rotaciones de esto costados y updown.
        private float leftrightRot;
        private float updownRot;

        private bool lockCam;
        private Vector3 positionEye;
        private bool isMoving = false;

        private bool collitionActive = true;
        private Vector3 lastSecurePos = new Vector3(0, 0, 0);
        //Manager de colisiones
        private CollisionCamera collisionManagerCamara;
        private readonly List<TgcBoundingAxisAlignBox> objetosCandidatos = new List<TgcBoundingAxisAlignBox>();

        //Jerarquia de Bounding Volumes para colisiones 
        public Core.BoundingVolumes.TgcBoundingSphere sphereCamara { get; set; }
        public Core.BoundingVolumes.TgcBoundingSphere sphereCamaraHead { get; set; }
        public TgcBoundingAxisAlignBox cajaHead { get; set; }

        private Core.Geometry.TgcBox cajaLoca = new Core.Geometry.TgcBox();

        public Vector3 getNewPosition()
        {
            return newPosition;
        }

        public TgcFpsCamera(TgcD3dInput input)
        {
            Input = input;
            positionEye = new Vector3();
            mouseCenter = new Point(
                D3DDevice.Instance.Device.Viewport.Width / 2,
                D3DDevice.Instance.Device.Viewport.Height / 2);
            RotationSpeed = 0.1f;
            MovementSpeed = 500f;
            JumpSpeed = 500f;
            directionView = new Vector3(0, 0, -1);
            leftrightRot = FastMath.PI_HALF;
            updownRot = -FastMath.PI / 10.0f;
            cameraRotation = Matrix.RotationX(updownRot) * Matrix.RotationY(leftrightRot);
        }

        public TgcFpsCamera(Vector3 positionEye, TgcD3dInput input) : this(input)
        {
            this.positionEye = positionEye;
        }

        public TgcFpsCamera(Vector3 positionEye, float moveSpeed, float jumpSpeed, TgcD3dInput input)
            : this(positionEye, input)
        {
            float x = positionEye.X;
            float y = positionEye.Y;
            float z = positionEye.Z;
            MovementSpeed = moveSpeed;
            JumpSpeed = jumpSpeed;
            cajaLoca.setPositionSize(new Vector3(x, y - 35f, z), new Vector3(5, 5, 5));
            Core.SceneLoader.TgcMesh laCajaLoca = cajaLoca.toMesh("laCajaLoca");
            cajaHead = new TgcBoundingAxisAlignBox(new Vector3(0,0,0),new Vector3(20,20,20),Position,new Vector3(1,1,1));
            sphereCamara = Core.BoundingVolumes.TgcBoundingSphere.computeFromMesh(laCajaLoca);
            sphereCamara.setValues(new Vector3(x, y - 40f, z), 15);
            sphereCamaraHead = new TgcBoundingSphere(new Vector3(x,y,z), 15);
            lockCam = true;
            collisionManagerCamara = new CollisionCamera();
            collisionManagerCamara.SlideFactor = 2.5f;
            //collisionManagerCamara.toggleGravity();
        }

        public TgcFpsCamera(Vector3 positionEye, float moveSpeed, float jumpSpeed, float rotationSpeed,
            TgcD3dInput input)
            : this(positionEye, moveSpeed, jumpSpeed, input)
        {
            RotationSpeed = rotationSpeed;
        }

        private TgcD3dInput Input { get; }

        public bool LockCam
        {
            get { return lockCam; }
            set
            {
                if (!lockCam && value)
                {
                    Cursor.Position = mouseCenter;

                    Cursor.Hide();
                }
                if (lockCam && !value)
                    Cursor.Show();
                lockCam = value;
            }
        }

        public float MovementSpeed { get; set; }

        public float RotationSpeed { get; set; }

        public float JumpSpeed { get; set; }

        public bool seMueve()
        {
            return isMoving;
        }

        /// <summary>
        ///     Cuando se elimina esto hay que desbloquear la camera.
        /// </summary>
        ~TgcFpsCamera()
        {
            LockCam = false;
        }

        public TgcBoundingSphere getCollisionHead()
        {
            return sphereCamaraHead;
        }

        public void UpdateCamera(float elapsedTime, List<Core.BoundingVolumes.TgcBoundingAxisAlignBox> obstaculos, float vidaPorcentaje,float staminaPorcentaje, bool playerHide, bool winCondition)
        {
            //Para el menu deberia ser Cursor.Show(); sino no ves donde haces click :P
            Cursor.Hide();
            var moveVector = new Vector3(0, 0, 0);
            Vector3 targetDistance = new Vector3(0, 0, 0);
            Vector3 targetDistanceNoSpeed = new Vector3(0, 0, 0);
            float epsilon = 0.05f;
            sphereCamara.setCenter(new Vector3(Position.X, Position.Y - 40, Position.Z));
            sphereCamaraHead.setCenter(Position);

            isMoving = false;

            if (playerHide == false )
            {

                #region Movimientos
                               
                #region Movimientos por Partes(Primero esfera luego camara)
                //Forward
                if (Input.keyDown(Key.W) && !Input.keyDown(Key.D) && !Input.keyDown(Key.A) && vidaPorcentaje > 0 && !winCondition)
                {
                    targetDistance += (LookAt - Position);
                    targetDistance.Y = 0;
                    targetDistance.Normalize();
                    targetDistanceNoSpeed = targetDistance;
                    targetDistance *= MovementSpeed;
                    if (collitionActive )
                    {
                            newPosition = collisionManagerCamara.moveCharacter(sphereCamara, targetDistance, obstaculos);
                            collisionManagerCamara.moveCharacter(sphereCamaraHead, targetDistanceNoSpeed, obstaculos);
                    }
                    else
                    {
                        moveVector += new Vector3(0, 0, -1) * MovementSpeed;
                    }
                    isMoving = true;
                }

                //Backward
                if (Input.keyDown(Key.S) && !Input.keyDown(Key.D) && !Input.keyDown(Key.A) && vidaPorcentaje > 0 && !winCondition)
                {
                    targetDistance -= (LookAt - Position);
                    targetDistance.Y = 0;
                    targetDistance.Normalize();
                    targetDistanceNoSpeed = targetDistance;
                    targetDistance *= MovementSpeed;

                    if (collitionActive)
                    {
                        newPosition = collisionManagerCamara.moveCharacter(sphereCamara, targetDistance, obstaculos);
                        collisionManagerCamara.moveCharacter(sphereCamaraHead, targetDistanceNoSpeed, obstaculos);
                    }
                    else
                    {
                        moveVector += new Vector3(0, 0, 1) * MovementSpeed;
                    }
                    isMoving = true;
                }

                //Strafe right
                if (Input.keyDown(Key.D) && !Input.keyDown(Key.W) && !Input.keyDown(Key.S) && vidaPorcentaje > 0 && !winCondition)
                {
                    targetDistance += Vector3.TransformNormal((new Vector3(LookAt.X, 0, LookAt.Z) - new Vector3(Position.X, 0, Position.Z)), Matrix.RotationY(FastMath.PI_HALF)) * (MovementSpeed);
                    if (collitionActive)
                    {
                        newPosition = collisionManagerCamara.moveCharacter(sphereCamara, targetDistance, obstaculos);
                    }
                    else
                    {
                        moveVector += new Vector3(-1, 0, 0) * MovementSpeed;
                    }
                    isMoving = true;
                }

                //Strafe left
                if (Input.keyDown(Key.A) && !Input.keyDown(Key.W) && !Input.keyDown(Key.S) && vidaPorcentaje > 0 && !winCondition)
                {
                    targetDistance += -Vector3.TransformNormal((new Vector3(LookAt.X, 0, LookAt.Z) - new Vector3(Position.X, 0, Position.Z)), Matrix.RotationY(FastMath.PI_HALF)) * (MovementSpeed);
                    if (collitionActive)
                    {
                        newPosition = collisionManagerCamara.moveCharacter(sphereCamara, targetDistance, obstaculos);
                    }
                    else
                    {
                        moveVector += new Vector3(1, 0, 0) * MovementSpeed;
                    }
                    isMoving = true;
                }

                //Forward + Strafe
                if (Input.keyDown(Key.W) && Input.keyDown(Key.D) && vidaPorcentaje > 0 && !winCondition)
                {
                    targetDistance += Vector3.TransformNormal((new Vector3(LookAt.X, 0, LookAt.Z) - new Vector3(Position.X, 0, Position.Z)), Matrix.RotationY(FastMath.QUARTER_PI));
                    targetDistance.Normalize();
                    targetDistanceNoSpeed = targetDistance;
                    targetDistance *= MovementSpeed;
                    if (collitionActive)
                    {
                        newPosition = collisionManagerCamara.moveCharacter(sphereCamara, targetDistance, obstaculos);
                        collisionManagerCamara.moveCharacter(sphereCamaraHead, targetDistanceNoSpeed, obstaculos);
                    }
                }
                if (Input.keyDown(Key.W) && Input.keyDown(Key.A) && vidaPorcentaje > 0 && !winCondition)
                {
                    targetDistance += -Vector3.TransformNormal((new Vector3(LookAt.X, 0, LookAt.Z) - new Vector3(Position.X, 0, Position.Z)), Matrix.RotationY(FastMath.QUARTER_PI));
                    targetDistance.Normalize();
                    targetDistanceNoSpeed = targetDistance;
                    targetDistance *= MovementSpeed;
                    if (collitionActive)
                    {
                        newPosition = collisionManagerCamara.moveCharacter(sphereCamara, targetDistance, obstaculos);
                        collisionManagerCamara.moveCharacter(sphereCamaraHead, targetDistanceNoSpeed, obstaculos);
                    }
                }

                //Backward + Strafe
                if (Input.keyDown(Key.S) && Input.keyDown(Key.D) && vidaPorcentaje > 0 && !winCondition)
                {
                    targetDistance += Vector3.TransformNormal((new Vector3(LookAt.X, 0, LookAt.Z) - new Vector3(Position.X, 0, Position.Z)), Matrix.RotationY(FastMath.QUARTER_PI));
                    targetDistance.Normalize();
                    targetDistanceNoSpeed = targetDistance;
                    targetDistance *= MovementSpeed;
                    if (collitionActive)
                    {
                        newPosition = collisionManagerCamara.moveCharacter(sphereCamara, targetDistance, obstaculos);
                        collisionManagerCamara.moveCharacter(sphereCamaraHead, targetDistanceNoSpeed, obstaculos);
                    }
                }
                if (Input.keyDown(Key.S) && Input.keyDown(Key.A) && vidaPorcentaje > 0 && !winCondition)
                {
                    targetDistance += -Vector3.TransformNormal((new Vector3(LookAt.X, 0, LookAt.Z) - new Vector3(Position.X, 0, Position.Z)), Matrix.RotationY(FastMath.QUARTER_PI));
                    targetDistance.Normalize();
                    targetDistanceNoSpeed = targetDistance;
                    targetDistance *= MovementSpeed;
                    if (collitionActive)
                    {
                        newPosition = collisionManagerCamara.moveCharacter(sphereCamara, targetDistance, obstaculos);
                        collisionManagerCamara.moveCharacter(sphereCamaraHead, targetDistanceNoSpeed, obstaculos);
                    }
                }

                Vector3 targetDistanceCam = new Vector3(0, 0, 0);
                Vector3 cameraOnGround = Position;
                cameraOnGround.Y = targetDistance.Y;
                if (Input.keyDown(Key.W) && !Input.keyDown(Key.D) && !Input.keyDown(Key.A) && vidaPorcentaje > 0 && !winCondition)
                {
                    targetDistanceCam += newPosition - cameraOnGround;
                    if(Input.keyDown(Key.LeftShift)) moveVector += new Vector3(0,newPosition.Y,-targetDistanceCam.Length()*0.3f);
                    else
                    {
                        moveVector += new Vector3(0, newPosition.Y, -targetDistanceCam.Length() * 0.15f);
                    }
                }
                if (Input.keyDown(Key.S) && !Input.keyDown(Key.D) && !Input.keyDown(Key.A) && vidaPorcentaje > 0 && !winCondition)
                {
                    targetDistanceCam += newPosition - cameraOnGround;
                    moveVector += new Vector3(0, 0, targetDistanceCam.Length() * 0.15f);
                }
                if (!Input.keyDown(Key.S) && Input.keyDown(Key.D) && !Input.keyDown(Key.W) && vidaPorcentaje > 0 && !winCondition)
                {
                    targetDistanceCam += newPosition - cameraOnGround;
                    moveVector += new Vector3( -targetDistanceCam.Length()*0.15f, 0, 0);
                    
                }
                if (!Input.keyDown(Key.S) && !Input.keyDown(Key.W) && Input.keyDown(Key.A) && vidaPorcentaje > 0 && !winCondition)
                {
                    targetDistanceCam += newPosition - cameraOnGround;
                    moveVector += new Vector3( targetDistanceCam.Length() * 0.15f, 0, 0);
                    
                }
                
                #endregion
            /*
            TgcBoundingSphere testSphereCamaraFall = new TgcBoundingSphere();
            testSphereCamaraFall = sphereCamara;
            testSphereCamaraFall.setCenter(new Vector3(testSphereCamaraFall.Center.X, testSphereCamaraFall.Center.Y-epsilon, testSphereCamaraFall.Center.Z));
            if (!checkCollision(obstaculos, testSphereCamaraFall) &&  (Position.X >= 1005 && Position.X <= 1234 && Position.Z >= 225 && Position.Z <= 335) && (Position.X >= 35 && Position.X <= 187 && Position.Z >= 1045 && Position.Z <= 1281))
            {
                moveVector.Y += 15;
            }
            */
            if (Position.Y <= 55 )
            {
                moveVector.Y = 56;
            }
            if (Position.Y > 110 && Position.Y <= 165 && !(Position.X >= 1005 && Position.X <= 1234 && Position.Z >= 225 && Position.Z <= 335) && !(Position.X >= 35 && Position.X <= 187 && Position.Z >= 1045 && Position.Z <= 1281))
            {
                moveVector.Y = 166;
            }

            #endregion

            #region Modificadores de movimiento
            //Fall
            if (!Input.keyDown(Key.W) || !Input.keyDown(Key.A) || !Input.keyDown(Key.D) || !Input.keyDown(Key.S) || !Input.keyDown(Key.Space))
            {
                newPosition = collisionManagerCamara.moveCharacter(sphereCamara, targetDistance, obstaculos);
                moveVector += new Vector3(0, newPosition.Y*2.4f, 0);
            }

            //Jump
            if (Input.keyPressed(Key.Space))
            {
                targetDistance += new Vector3(0,1,0) * JumpSpeed;
                if (collitionActive)
                {
                    newPosition = collisionManagerCamara.moveCharacter(sphereCamara, targetDistance, obstaculos);
                    moveVector += new Vector3(0, newPosition.Y + 3*JumpSpeed, 0) ;
                }
                else
                {
                    moveVector += new Vector3(0, 1, 0) * JumpSpeed;
                }               
            }

            //Crouch
            if (Input.keyDown(Key.LeftControl))
            {
                moveVector += new Vector3(0, -1, 0) * JumpSpeed;
            }

            if (Input.keyPressed(Key.L) || Input.keyPressed(Key.Escape))
            {
                collitionActive = !collitionActive;
                //LockCam = !lockCam;
            }

            if (Input.keyDown(Key.LeftShift) && Input.keyDown(Key.W) && staminaPorcentaje > 0)
            {
                MovementSpeed = 200f;
            }
            else
            {
                MovementSpeed = 100f;
            }
                #endregion

            }
            //Rotacion de la camara
            if (lockCam )
            {
                leftrightRot -= -Input.XposRelative * RotationSpeed;

                //Verificamos que la rotacion vertical este dentro de un angulo determinado
                float anguloLimite = 0.91f;
                if ( LookAt.Y - Position.Y < anguloLimite && LookAt.Y - Position.Y > -anguloLimite)
                {
                    updownRot -= Input.YposRelative * RotationSpeed;
                }
                else
                {
                    if(LookAt.Y - Position.Y >= anguloLimite)
                    {
                        updownRot -= 0.5f * RotationSpeed;
                    }
                    if (LookAt.Y - Position.Y <= -anguloLimite)
                    {
                        updownRot += 0.5f * RotationSpeed;
                    }
                }
                
                //Se actualiza matrix de rotacion, para no hacer este calculo cada vez y solo cuando en verdad es necesario.
                cameraRotation = Matrix.RotationY(leftrightRot);
            }

            if (lockCam) Cursor.Position = mouseCenter;

            //Calculamos la nueva posicion del ojo segun la rotacion actual de la camara.
            var cameraRotatedPositionEye = Vector3.TransformNormal(moveVector * elapsedTime, cameraRotation);
            positionEye += cameraRotatedPositionEye;
           
            cameraRotation = Matrix.RotationX(updownRot) * Matrix.RotationY(leftrightRot);
            cameraRotatedPositionEye = Vector3.TransformNormal(moveVector * elapsedTime, cameraRotation);
         
            //Calculamos el target de la camara, segun su direccion inicial y las rotaciones en screen space x,y.

            var cameraRotatedTarget = Vector3.TransformNormal( directionView, cameraRotation);
            var cameraFinalTarget = positionEye + cameraRotatedTarget;
           
            var cameraOriginalUpVector = DEFAULT_UP_VECTOR;
            var cameraRotatedUpVector = Vector3.TransformNormal(cameraOriginalUpVector, cameraRotation);
            cajaHead.move(positionEye);
            //sphereCamara.moveCenter(new Vector3(positionEye.X,positionEye.Y-40,positionEye.Z));
            base.SetCamera(positionEye, cameraFinalTarget, cameraRotatedUpVector);
        }

        /// <summary>
        ///     se hace override para actualizar las posiones internas, estas seran utilizadas en el proximo update.
        /// </summary>
        /// <param name="position"></param>
        /// <param name="directionView"> debe ser normalizado.</param>
        public override void SetCamera(Vector3 position, Vector3 directionView)
        {
            positionEye = position;
            this.directionView = directionView;
        }

        public TgcBoundingAxisAlignBox getCajaHead()
        {
            return cajaHead;
        }

        public void render(float elapsedTime, List<Core.BoundingVolumes.TgcBoundingAxisAlignBox> obstaculos)
        {
            sphereCamaraHead.render();
            sphereCamara.render();
            cajaHead.render();
        }
 
        public float distancia(Vector3 a,Vector3 b)
        {
            return (FastMath.Sqrt(FastMath.Pow2(a.X - b.X) + FastMath.Pow2(a.Y - b.Y) + FastMath.Pow2(a.Z - b.Z)));
        }

    }
}